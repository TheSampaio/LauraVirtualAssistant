using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Laura.Core.Permissions;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Implementation of <see cref="IProcessLauncher"/> over the Windows shell.
///
/// All failures become <see langword="false"/> instead of exceptions: asking for an
/// application that does not exist is normal voice-assistant use, not a defect.
/// </summary>
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    private readonly ILogger<WindowsProcessLauncher> _logger;
    private readonly WindowsApplicationCatalog _applications;
    private readonly PermissionManager _permissions;

    /// <summary>
    /// Initializes the launcher.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WindowsProcessLauncher(ILogger<WindowsProcessLauncher> logger, WindowsApplicationCatalog applications, PermissionManager permissions)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _applications = applications;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // Legacy synchronous callers cannot display asynchronous approval or bypass it.
        return false;
    }

    /// <inheritdoc />
    public bool TryStart(string fileName, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> TryOpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https" or "ms-settings")) { return false; }
        string address = uri.AbsoluteUri;
        try
        {
            return await _permissions.ExecuteAsync(new(PermissionOperation.ExternalNavigation, "Windows launcher", address,
                parameters: address, destination: uri.Scheme == "ms-settings" ? "" : uri.Host),
                token => { token.ThrowIfCancellationRequested(); return Task.FromResult(TryStartProcess(new(address) { UseShellExecute = true }, "external navigation")); }, cancellationToken).ConfigureAwait(false);
        }
        catch (PermissionDeniedException) { return false; }
    }

    /// <inheritdoc />
    public async Task<bool> TryStartAsync(string fileName, string? arguments = null, CancellationToken cancellationToken = default)
        => (await TryStartObservedAsync(fileName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false)).Accepted;

    /// <inheritdoc />
    public async Task<ProcessLaunchResult> TryStartObservedAsync(string fileName, string? arguments = null, string? expectedHash = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (expectedHash is not null && (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))) { return new(false, fileName, Reason: "Invalid expected identity.") { StopFallback = true }; }
        if (PermissionPolicy.IsNetworkPath(fileName)) { return new(false, fileName, Reason: "Network launch targets are unsupported.") { StopFallback = true }; }
        if (!Path.IsPathFullyQualified(fileName) && _applications.Applications.Count == 0) { await _applications.RefreshAsync(cancellationToken).ConfigureAwait(false); }
        IReadOnlyList<InstalledApplication> matches = File.Exists(fileName) ? [] : _applications.Find(fileName);
        if (matches.Count > 1 && Laura.Core.Skills.ApplicationNameMatcher.Score(matches[0].Name, fileName) == Laura.Core.Skills.ApplicationNameMatcher.Score(matches[1].Name, fileName))
        { return new(false, fileName, Reason: "Ambiguous application name.") { StopFallback = true }; }
        string target = matches.Count > 0 ? matches[0].Target : fileName;
        try { if (!File.Exists(target)) { target = ResolveExecutable(target); } target = Path.GetFullPath(target); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return new(false, target, Reason: "Executable unavailable."); }
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Arguments = arguments ?? string.Empty,
        };
        if (Environment.GetCommandLineArgs().Contains("--brain-smoke", StringComparer.Ordinal)) { startInfo.WindowStyle = ProcessWindowStyle.Hidden; }

        try
        {
            var action = new PermissionAction(PermissionOperation.Process, "Windows launcher", target + "\n" + arguments,
                [target], JsonSerializer.Serialize(new { arguments, expectedHash }));
            return await _permissions.ExecuteAsync(action, token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); PermissionPolicy.ValidateResources(action, []);
                try
                {
                    using FileStream? artifact = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                    string hash = artifact is null || artifact.Length > 64 * 1024 * 1024 ? "" : Convert.ToHexString(SHA256.HashData(artifact));
                    if (expectedHash is not null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) { return new ProcessLaunchResult(false, target, Reason: "Learned executable changed.") { StopFallback = true }; }
                    token.ThrowIfCancellationRequested();
                    using Process? process = Process.Start(startInfo);
                    return new ProcessLaunchResult(true, target, process?.Id, hash);
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                { _logger.LogWarning("Application launch failed ({ErrorType}).", error.GetType().Name); return new(false, target, Reason: "Windows refused launch."); }
            }, token), cancellationToken).ConfigureAwait(false);
        }
        catch (PermissionDeniedException) { return new(false, target, Reason: "Authorization denied.") { StopFallback = true }; }
    }

    /// <summary>Resolves executables before approval so PATH changes cannot redirect an approved launch.</summary>
    public static string ResolveExecutable(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (Path.IsPathFullyQualified(executable)) { return Path.GetFullPath(executable); }
        if (executable.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0)
        { throw new ArgumentException("Use a full executable path.", nameof(executable)); }
        string name = Path.HasExtension(executable) ? executable : executable + ".exe";
        foreach (string directory in new[] { Environment.SystemDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)))
        {
            if (!Path.IsPathFullyQualified(directory)) { continue; }
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
        }
        throw new FileNotFoundException("Executable could not be resolved.");
    }

    /// <summary>
    /// Starts a process while catching expected failures.
    ///
    /// Args:
    ///     startInfo: Description of the process to start.
    ///     target: Target description, used only in diagnostic logging.
    ///
    /// Returns:
    ///     <see langword="true"/> when the process was started.
    /// </summary>
    private bool TryStartProcess(ProcessStartInfo startInfo, string target)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning("Could not open {Target} ({ErrorType}).", target, exception.GetType().Name);
            return false;
        }
    }
}

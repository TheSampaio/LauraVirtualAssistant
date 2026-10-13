using System.ComponentModel;
using System.Diagnostics;
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (PermissionPolicy.IsNetworkPath(fileName)) { return false; }
        if (_applications.Applications.Count == 0) { await _applications.RefreshAsync(cancellationToken).ConfigureAwait(false); }
        IReadOnlyList<InstalledApplication> matches = File.Exists(fileName) ? [] : _applications.Find(fileName);
        string target = matches.Count > 0 ? matches[0].Target : fileName;
        if (!File.Exists(target)) { target = ResolveExecutable(target); }
        target = Path.GetFullPath(target);
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Arguments = arguments ?? string.Empty,
        };

        try
        {
            return await _permissions.ExecuteAsync(new(PermissionOperation.Process, "Windows launcher", target + "\n" + arguments,
                [target], arguments ?? ""), token => { token.ThrowIfCancellationRequested(); return Task.FromResult(TryStartProcess(startInfo, "approved application")); }, cancellationToken).ConfigureAwait(false);
        }
        catch (PermissionDeniedException) { return false; }
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
            _logger.LogWarning(exception, "Could not open {Target}.", target);
            return false;
        }
    }
}

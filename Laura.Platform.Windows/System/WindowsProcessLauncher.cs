using System.ComponentModel;
using System.Diagnostics;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Initializes the launcher.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WindowsProcessLauncher(ILogger<WindowsProcessLauncher> logger, WindowsApplicationCatalog applications)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _applications = applications;
    }

    /// <inheritdoc />
    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return TryStartProcess(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }, uri.ToString());
    }

    /// <inheritdoc />
    public bool TryStart(string fileName, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (_applications.Applications.Count == 0) { _applications.RefreshAsync().GetAwaiter().GetResult(); }
        IReadOnlyList<InstalledApplication> matches = File.Exists(fileName) ? [] : _applications.Find(fileName);
        string target = matches.Count > 0 ? matches[0].Target : fileName;
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Arguments = arguments ?? string.Empty,
        };

        return TryStartProcess(startInfo, target);
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

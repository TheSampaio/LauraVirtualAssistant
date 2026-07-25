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

    /// <summary>
    /// Inicializa o executor.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WindowsProcessLauncher(ILogger<WindowsProcessLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
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

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Arguments = arguments ?? string.Empty,
        };

        return TryStartProcess(startInfo, fileName);
    }

    /// <summary>
    /// Inicia um processo capturando as falhas esperadas.
    ///
    /// Args:
    ///     startInfo: Description of the process to start.
    ///     target: Target description, used only in diagnostic logging.
    ///
    /// Returns:
    ///     <see langword="true"/> quando o processo foi iniciado.
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

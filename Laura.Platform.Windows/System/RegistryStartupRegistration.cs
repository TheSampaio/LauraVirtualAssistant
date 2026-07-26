using System.Diagnostics;
using System.Security;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Registers automatic startup in the current user's <c>Run</c> key.
///
/// The per-user key is used deliberately: it does not require elevation and does not affect other
/// accounts on the machine.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Laura";

    private readonly ILogger<RegistryStartupRegistration> _logger;

    /// <summary>
    /// Initializes the registry registration.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public RegistryStartupRegistration(ILogger<RegistryStartupRegistration> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read the automatic startup registry entry.");
            return false;
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        string executablePath = GetExecutablePath();

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _logger.LogWarning("Executable path unavailable; automatic startup was skipped.");
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not write the automatic startup registry entry.");
        }
    }

    /// <summary>
    /// Returns the executable path in use, for diagnostics.
    ///
    /// Returns:
    ///     The current process path, or an empty string when unavailable.
    /// </summary>
    internal static string GetExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
}

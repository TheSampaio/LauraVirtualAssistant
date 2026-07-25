using System.Diagnostics;
using System.Security;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Registra a inicialização automática na chave <c>Run</c> do usuário atual.
///
/// A chave por usuário é usada de propósito: não exige elevação e não afeta outras
/// contas da máquina.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Laura";

    private readonly ILogger<RegistryStartupRegistration> _logger;

    /// <summary>
    /// Inicializa o registro.
    ///
    /// Args:
    ///     logger: Destino dos registros de diagnóstico.
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
            _logger.LogWarning(exception, "Não foi possível ler o registro de inicialização automática.");
            return false;
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        string? executablePath = Environment.ProcessPath;

        if (executablePath is null)
        {
            _logger.LogWarning("Caminho do executável indisponível; a inicialização automática foi ignorada.");
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
            _logger.LogWarning(exception, "Não foi possível gravar o registro de inicialização automática.");
        }
    }

    /// <summary>
    /// Devolve o caminho do executável em uso, para diagnóstico.
    ///
    /// Returns:
    ///     O caminho do processo atual, ou uma string vazia quando indisponível.
    /// </summary>
    internal static string GetExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
}

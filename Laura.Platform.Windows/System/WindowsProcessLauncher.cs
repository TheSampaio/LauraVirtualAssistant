using System.ComponentModel;
using System.Diagnostics;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Implementação de <see cref="IProcessLauncher"/> sobre o shell do Windows.
///
/// Todas as falhas viram <see langword="false"/> em vez de exceção: pedir um
/// aplicativo que não existe é uso normal de uma assistente de voz, não um defeito.
/// </summary>
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    private readonly ILogger<WindowsProcessLauncher> _logger;

    /// <summary>
    /// Inicializa o executor.
    ///
    /// Args:
    ///     logger: Destino dos registros de diagnóstico.
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
    ///     startInfo: Descrição do processo a iniciar.
    ///     target: Descrição do alvo, usada apenas no registro de diagnóstico.
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
            _logger.LogWarning(exception, "Não foi possível abrir {Target}.", target);
            return false;
        }
    }
}

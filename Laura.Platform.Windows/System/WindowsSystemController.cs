using Laura.Core.Abstractions;
using Laura.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Implementação de <see cref="ISystemController"/> sobre a API do Windows.
/// </summary>
public sealed class WindowsSystemController : ISystemController
{
    private readonly ILogger<WindowsSystemController> _logger;

    /// <summary>
    /// Inicializa o controlador.
    ///
    /// Args:
    ///     logger: Destino dos registros de diagnóstico.
    /// </summary>
    public WindowsSystemController(ILogger<WindowsSystemController> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void LockWorkstation()
    {
        if (!NativeMethods.LockWorkStation())
        {
            _logger.LogWarning("O Windows recusou o pedido de bloqueio da estação de trabalho.");
        }
    }

    /// <inheritdoc />
    public void AdjustVolume(int steps)
    {
        byte key = steps >= 0 ? NativeMethods.VirtualKeyVolumeUp : NativeMethods.VirtualKeyVolumeDown;

        for (int index = 0; index < Math.Abs(steps); index++)
        {
            PressKey(key);
        }
    }

    /// <inheritdoc />
    public void ToggleMute() => PressKey(NativeMethods.VirtualKeyVolumeMute);

    /// <summary>
    /// Simula o pressionar e soltar de uma tecla multimídia.
    ///
    /// Args:
    ///     virtualKey: Código virtual da tecla.
    /// </summary>
    private static void PressKey(byte virtualKey)
    {
        NativeMethods.SendKeyboardEvent(virtualKey, scanCode: 0, flags: 0, extraInfo: 0);
        NativeMethods.SendKeyboardEvent(virtualKey, scanCode: 0, NativeMethods.KeyEventKeyUp, extraInfo: 0);
    }
}

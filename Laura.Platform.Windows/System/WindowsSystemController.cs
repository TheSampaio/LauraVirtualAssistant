using Laura.Core.Abstractions;
using Laura.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Laura.Platform.Windows.System;

/// <summary>
/// Implementation of <see cref="ISystemController"/> over the Windows API.
/// </summary>
public sealed class WindowsSystemController : ISystemController
{
    private readonly ILogger<WindowsSystemController> _logger;

    /// <summary>
    /// Inicializa o controlador.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
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
            _logger.LogWarning("Windows refused the workstation lock request.");
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
    /// Simulates pressing and releasing a multimedia key.
    ///
    /// Args:
    ///     virtualKey: Virtual key code.
    /// </summary>
    private static void PressKey(byte virtualKey)
    {
        NativeMethods.SendKeyboardEvent(virtualKey, scanCode: 0, flags: 0, extraInfo: 0);
        NativeMethods.SendKeyboardEvent(virtualKey, scanCode: 0, NativeMethods.KeyEventKeyUp, extraInfo: 0);
    }
}

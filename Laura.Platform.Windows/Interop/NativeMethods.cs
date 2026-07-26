using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.Interop;

/// <summary>
/// Windows API functions used by platform adapters.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Virtual key code for mute.</summary>
    internal const byte VirtualKeyVolumeMute = 0xAD;

    /// <summary>Virtual key code for volume down.</summary>
    internal const byte VirtualKeyVolumeDown = 0xAE;

    /// <summary>Virtual key code for volume up.</summary>
    internal const byte VirtualKeyVolumeUp = 0xAF;

    /// <summary>Signals that the keyboard event corresponds to key release.</summary>
    internal const uint KeyEventKeyUp = 0x0002;

    /// <summary>
    /// Locks the workstation, like the Windows + L combination.
    ///
    /// Returns:
    ///     <see langword="true"/> when the lock request was accepted.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LockWorkStation();

    /// <summary>
    /// Synthesizes a keyboard event.
    ///
    /// This is how multimedia volume keys are triggered without involving COM or the
    /// audio mixer: the Windows shell already handles these codes.
    ///
    /// Args:
    ///     virtualKey: Virtual key code.
    ///     scanCode: Hardware scan code; zero is accepted.
    ///     flags: Event flags, such as <see cref="KeyEventKeyUp"/>.
    ///     extraInfo: Additional value associated with the event.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
    internal static partial void SendKeyboardEvent(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}

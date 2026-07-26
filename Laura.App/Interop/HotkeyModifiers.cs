namespace Laura.App.Interop;

/// <summary>
/// Modifier keys accepted by a global hotkey.
///
/// The values mirror the Windows API <c>MOD_*</c> constants.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    /// <summary>No modifier.</summary>
    None = 0x0000,

    /// <summary>Alt key.</summary>
    Alt = 0x0001,

    /// <summary>Ctrl key.</summary>
    Control = 0x0002,

    /// <summary>Shift key.</summary>
    Shift = 0x0004,

    /// <summary>Windows key.</summary>
    Windows = 0x0008,

    /// <summary>Prevents automatic repeat while the combination is held down.</summary>
    NoRepeat = 0x4000,
}

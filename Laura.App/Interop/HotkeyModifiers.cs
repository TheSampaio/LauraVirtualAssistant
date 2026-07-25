namespace Laura.App.Interop;

/// <summary>
/// Teclas modificadoras aceitas por uma tecla de atalho global.
///
/// Os valores espelham as constantes <c>MOD_*</c> da API do Windows.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    /// <summary>Nenhum modificador.</summary>
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

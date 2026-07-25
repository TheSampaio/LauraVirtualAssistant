using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.Interop;

/// <summary>
/// Funções da API do Windows usadas pelos adaptadores de plataforma.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Código da tecla multimídia de silenciar.</summary>
    internal const byte VirtualKeyVolumeMute = 0xAD;

    /// <summary>Código da tecla multimídia de diminuir volume.</summary>
    internal const byte VirtualKeyVolumeDown = 0xAE;

    /// <summary>Código da tecla multimídia de aumentar volume.</summary>
    internal const byte VirtualKeyVolumeUp = 0xAF;

    /// <summary>Sinaliza que o evento de teclado corresponde à soltura da tecla.</summary>
    internal const uint KeyEventKeyUp = 0x0002;

    /// <summary>
    /// Bloqueia a estação de trabalho, como faz a combinação Windows + L.
    ///
    /// Returns:
    ///     <see langword="true"/> quando o pedido de bloqueio foi aceito.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LockWorkStation();

    /// <summary>
    /// Sintetiza um evento de teclado.
    ///
    /// É como as teclas multimídia de volume são acionadas sem envolver COM nem o
    /// mixer de áudio: o shell do Windows já trata esses códigos.
    ///
    /// Args:
    ///     virtualKey: Código virtual da tecla.
    ///     scanCode: Código de varredura do hardware; zero é aceito.
    ///     flags: Sinalizadores do evento, como <see cref="KeyEventKeyUp"/>.
    ///     extraInfo: Valor adicional associado ao evento.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
    internal static partial void SendKeyboardEvent(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}

using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Laura.App.Interop;

/// <summary>
/// Registra uma tecla de atalho global e avisa quando ela é pressionada.
///
/// Usa uma janela-mensagem oculta em vez de um gancho de teclado: <c>RegisterHotKey</c>
/// é tratado pelo próprio Windows, dispensando um gancho de baixo nível que
/// inspecionaria cada tecla do sistema — mais leve e menos intrusivo.
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C41; // "LA", para não colidir com outros registros do processo.

    private readonly MessageWindow _window;
    private bool _registered;
    private bool _disposed;

    /// <summary>
    /// Cria a janela-mensagem e registra a combinação de teclas.
    ///
    /// Privado porque o registro pode falhar legitimamente — outra aplicação já pode
    /// ter reservado a combinação. Use <see cref="TryRegister"/>.
    ///
    /// Args:
    ///     modifiers: Modificadores exigidos, como Alt.
    ///     key: Tecla principal da combinação.
    /// </summary>
    private GlobalHotkey(HotkeyModifiers modifiers, Keys key)
    {
        _window = new MessageWindow();
        _window.HotkeyPressed += (_, _) => Pressed?.Invoke(this, EventArgs.Empty);
        _registered = RegisterHotKey(_window.Handle, HotkeyId, (uint)modifiers, (uint)key);
    }

    /// <summary>
    /// Tenta registrar uma tecla de atalho global.
    ///
    /// Não lança: uma combinação já tomada por outro programa é situação normal, e
    /// a aplicação precisa seguir funcionando sem o atalho.
    ///
    /// Args:
    ///     modifiers: Modificadores exigidos, como Alt.
    ///     key: Tecla principal da combinação.
    ///
    /// Returns:
    ///     A tecla de atalho registrada, ou <see langword="null"/> quando o Windows
    ///     recusou o registro.
    /// </summary>
    public static GlobalHotkey? TryRegister(HotkeyModifiers modifiers, Keys key)
    {
        var hotkey = new GlobalHotkey(modifiers, key);

        if (hotkey.IsRegistered)
        {
            return hotkey;
        }

        hotkey.Dispose();
        return null;
    }

    /// <summary>
    /// Obtém um valor que indica se a combinação foi efetivamente registrada.
    /// </summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Ocorre quando a combinação registrada é pressionada.
    /// </summary>
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }

        _window.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>
    /// Janela invisível que recebe as mensagens de tecla de atalho.
    /// </summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        /// <summary>
        /// Cria a janela-mensagem, sem interface visível.
        /// </summary>
        public MessageWindow() => CreateHandle(new CreateParams());

        /// <summary>
        /// Ocorre quando chega a mensagem de tecla de atalho.
        /// </summary>
        public event EventHandler? HotkeyPressed;

        /// <inheritdoc />
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }

            base.WndProc(ref message);
        }

        /// <inheritdoc />
        public void Dispose() => DestroyHandle();
    }
}

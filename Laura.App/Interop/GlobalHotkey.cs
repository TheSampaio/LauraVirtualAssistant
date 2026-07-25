using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Laura.App.Interop;

/// <summary>
/// Registers a global hotkey and notifies when it is pressed.
///
/// Uses a hidden message-only window instead of a keyboard hook: <c>RegisterHotKey</c>
/// is handled by Windows itself, avoiding a low-level hook that
/// would inspect every system keypress - lighter and less intrusive.
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C41; // "LA", to avoid colliding with other registrations in the process.

    private readonly MessageWindow _window;
    private bool _registered;
    private bool _disposed;

    /// <summary>
    /// Creates the message-only window and registers the key combination.
    ///
    /// Private because registration can legitimately fail - another application may already
    /// have reserved the combination. Use <see cref="TryRegister"/>.
    ///
    /// Args:
    ///     modifiers: Required modifiers, such as Alt.
    ///     key: Main key in the combination.
    /// </summary>
    private GlobalHotkey(HotkeyModifiers modifiers, Keys key)
    {
        _window = new MessageWindow();
        _window.HotkeyPressed += (_, _) => Pressed?.Invoke(this, EventArgs.Empty);
        _registered = RegisterHotKey(_window.Handle, HotkeyId, (uint)modifiers, (uint)key);
    }

    /// <summary>
    /// Attempts to register a global hotkey.
    ///
    /// Does not throw: a combination already taken by another program is normal, and
    /// the application must keep working without the shortcut.
    ///
    /// Args:
    ///     modifiers: Required modifiers, such as Alt.
    ///     key: Main key in the combination.
    ///
    /// Returns:
    ///     The registered hotkey, or <see langword="null"/> when Windows
    ///     refused the registration.
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
    /// Gets a value indicating whether the combination was actually registered.
    /// </summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Occurs when the registered combination is pressed.
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
    /// Invisible window that receives hotkey messages.
    /// </summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        /// <summary>
        /// Creates the message-only window, with no visible interface.
        /// </summary>
        public MessageWindow() => CreateHandle(new CreateParams());

        /// <summary>
        /// Occurs when the hotkey message arrives.
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

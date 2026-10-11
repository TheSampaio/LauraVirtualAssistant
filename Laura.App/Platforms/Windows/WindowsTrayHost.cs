using System.Runtime.InteropServices;

namespace Laura.App.Platforms.Windows;

/// <summary>Native tray and Alt+L integration for a WinUI window, without Windows Forms.</summary>
public sealed class WindowsTrayHost : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private readonly nint _window;
    private readonly SubclassProcedure _procedure;
    private readonly Action _toggle;
    private readonly Action _exit;
    private readonly Action<bool>? _visibility;
    private readonly nint _icon;
    private bool _exiting;
    private bool _visible = true;
    private readonly uint _taskbarCreated;
    /// <summary>Registers the hotkey and attaches a notification icon to the existing MAUI handle.</summary>
    public WindowsTrayHost(nint window, Action toggle, Action exit, Action<bool>? visibility = null)
    {
        _window = window; _toggle = toggle; _exit = exit; _procedure = WindowProcedure;
        _visibility = visibility;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"), 1, 32, 32, 0x10);
        SetWindowSubclass(window, _procedure, 1, 0);
        RegisterHotKey(window, 1, 0x4001, 0x4C);
        AddIcon();
    }
    /// <summary>Shows the native window and gives it keyboard focus.</summary>
    public void Show() { ShowWindow(_window, 9); SetForegroundWindow(_window); _visible = true; _visibility?.Invoke(true); }
    /// <summary>Hides the window while the assistant remains available in the tray.</summary>
    public void Hide() { ShowWindow(_window, 0); _visible = false; _visibility?.Invoke(false); }
    /// <summary>Toggles native visibility for the tray and global hotkey.</summary>
    public void Toggle() { if (_visible) { Hide(); } else { Show(); } }
    /// <summary>Allows window destruction during an explicit application exit.</summary>
    public void AllowExit() => _exiting = true;
    /// <inheritdoc />
    public void Dispose()
    {
        _exiting = true;
        var data = IconData(); ShellNotifyIcon(2, ref data);
        UnregisterHotKey(_window, 1); RemoveWindowSubclass(_window, _procedure, 1);
        if (_icon != 0) { DestroyIcon(_icon); }
    }
    /// <summary>Restores the notification icon after Explorer restarts.</summary>
    private void AddIcon() { var data = IconData(); ShellNotifyIcon(0, ref data); }
    /// <summary>Builds the native notification icon structure.</summary>
    private NotifyIconData IconData() => new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = 7, CallbackMessage = TrayMessage, Icon = _icon, Tip = "Laura · Alt + L", Info = "", InfoTitle = "" };
    /// <summary>Handles tray activation, close-to-tray and the global hotkey.</summary>
    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x10 && !_exiting) { Hide(); return 0; }
        if (message == 0x312) { _toggle(); return 0; }
        if (message == _taskbarCreated) { AddIcon(); }
        if (message == TrayMessage)
        {
            if ((uint)lParam == 0x202) { _toggle(); }
            if ((uint)lParam == 0x205)
            {
                nint menu = CreatePopupMenu();
                try
                {
                    AppendMenu(menu, 0, 1, "Laura · Alt + L"); AppendMenu(menu, 0, 2, "Exit / Sair");
                    GetCursorPos(out NativePoint point); SetForegroundWindow(window);
                    uint command = TrackPopupMenu(menu, 0x100, point.X, point.Y, 0, window, 0);
                    if (command == 1) { Show(); } else if (command == 2) { _exit(); }
                }
                finally { DestroyMenu(menu); }
            }
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size; internal nint Window; internal uint Id; internal uint Flags; internal uint CallbackMessage; internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State; internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags; internal Guid Guid; internal nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X; internal int Y; }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}

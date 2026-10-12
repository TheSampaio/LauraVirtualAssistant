using System.Runtime.InteropServices;
using Laura.App.Theming;
using Microsoft.UI.Windowing;

namespace Laura.App.Platforms.Windows;

/// <summary>Applies Laura's dark palette to native window chrome and caption buttons.</summary>
public static class WindowsWindowTheme
{
    /// <summary>Applies dark native chrome with only the close caption button.</summary>
    public static void Apply(Microsoft.UI.Xaml.Window window)
    {
        if (window.Content is Microsoft.UI.Xaml.FrameworkElement root) { root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark; }
        AppWindowTitleBar title = window.AppWindow.TitleBar;
        title.PreferredTheme = TitleBarTheme.Dark;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            title.BackgroundColor = NativeColor(Theme.Background); title.ForegroundColor = NativeColor(Colors.White);
            title.InactiveBackgroundColor = NativeColor(Theme.Background); title.InactiveForegroundColor = NativeColor(Colors.White);
            title.ButtonBackgroundColor = NativeColor(Theme.Background); title.ButtonForegroundColor = NativeColor(Colors.White);
            title.ButtonInactiveBackgroundColor = NativeColor(Theme.Background); title.ButtonInactiveForegroundColor = NativeColor(Colors.White);
            title.ButtonHoverBackgroundColor = NativeColor(Theme.Field); title.ButtonHoverForegroundColor = NativeColor(Colors.White);
            title.ButtonPressedBackgroundColor = NativeColor(Theme.Accent); title.ButtonPressedForegroundColor = NativeColor(Colors.White);
        }
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        int background = ColorRef(Theme.Background), foreground = ColorRef(Colors.White);
        _ = DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref foreground, sizeof(int));
        ConfigureCaption(window);
        window.AppWindow.Changed += (_, args) => { if (args.DidPresenterChange) { ConfigureCaption(window); } };
    }
    /// <summary>Removes both caption styles while preserving the native close action and manual resizing.</summary>
    internal static void ConfigureCaption(Microsoft.UI.Xaml.Window window)
    {
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter) { return; }
        if (presenter.IsMinimizable) { presenter.IsMinimizable = false; }
        if (presenter.IsMaximizable) { presenter.IsMaximizable = false; }
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        nint style = GetWindowLongPtr(handle, -16);
        if ((style.ToInt64() & 0x00030000L) != 0)
        {
            _ = SetWindowLongPtr(handle, -16, new nint(style.ToInt64() & ~0x00030000L));
            _ = SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037);
        }
    }
    /// <summary>Centers the normal window in the nearest display's usable area, fitting smaller displays without changing fullscreen geometry.</summary>
    internal static void CenterWindow(nint handle)
    {
        AppWindow appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));
        if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen || !GetWindowRect(handle, out NativeRectangle bounds)) { return; }
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) { return; }
        int width = Math.Min(bounds.Right - bounds.Left, info.Work.Right - info.Work.Left);
        int height = Math.Min(bounds.Bottom - bounds.Top, info.Work.Bottom - info.Work.Top);
        _ = SetWindowPos(handle, 0, info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2,
            info.Work.Top + (info.Work.Bottom - info.Work.Top - height) / 2, width, height, 0x0014);
    }
    /// <summary>Uses the same fullscreen transition for F11 and isolated native diagnostics.</summary>
    internal static void ToggleFullscreen(Microsoft.UI.Xaml.Window window)
    {
        bool returning = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        window.AppWindow.SetPresenter(returning ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
        if (returning) { ConfigureCaption(window); CenterWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)); }
    }
    /// <summary>Verifies actual native style bits and geometry, rather than only managed presenter properties.</summary>
    internal static void VerifyWindowForSmoke(Microsoft.UI.Xaml.Window window)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Window diagnostics require isolated smoke mode."); }
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(handle, out NativeRectangle bounds) || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) { throw new InvalidOperationException("Native window geometry is unavailable."); }
        if (window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            if (bounds.Left != info.Monitor.Left || bounds.Top != info.Monitor.Top || bounds.Right != info.Monitor.Right || bounds.Bottom != info.Monitor.Bottom) { throw new InvalidOperationException("Fullscreen geometry does not cover the monitor."); }
            return;
        }
        if ((GetWindowLongPtr(handle, -16).ToInt64() & 0x00030000L) != 0) { throw new InvalidOperationException("Minimize or maximize caption styles are still present."); }
        if (Math.Abs(bounds.Left + bounds.Right - info.Work.Left - info.Work.Right) > 2 || Math.Abs(bounds.Top + bounds.Bottom - info.Work.Top - info.Work.Bottom) > 2)
        { throw new InvalidOperationException("The shown window is not centered in the monitor work area."); }
    }
    /// <summary>Tests the actual close message and centered reopening in an isolated app, preserving the fullscreen presenter when active.</summary>
    internal static void VerifyCloseAndReopenForSmoke(Microsoft.UI.Xaml.Window window, Action show)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Window diagnostics require isolated smoke mode."); }
        bool fullscreen = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        if (!fullscreen) { var position = window.AppWindow.Position; window.AppWindow.Move(new global::Windows.Graphics.PointInt32 { X = position.X + 40, Y = position.Y + 24 }); }
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = SendMessage(handle, 0x10, 0, 0);
        if (IsWindowVisible(handle)) { throw new InvalidOperationException("Close did not hide the window in the tray."); }
        show();
        if (!IsWindowVisible(handle) || (window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) != fullscreen) { throw new InvalidOperationException("Reopening changed visibility or the fullscreen presenter."); }
        VerifyWindowForSmoke(window);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRectangle { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal NativeRectangle Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    /// <summary>Converts a shared MAUI color to the Windows ARGB title-bar value.</summary>
    private static global::Windows.UI.Color NativeColor(Color color) => global::Windows.UI.Color.FromArgb(255, (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255));
    /// <summary>Converts a shared color to the BGR value used by Desktop Window Manager.</summary>
    private static int ColorRef(Color color) => (int)(color.Red * 255) | ((int)(color.Green * 255) << 8) | ((int)(color.Blue * 255) << 16);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
}

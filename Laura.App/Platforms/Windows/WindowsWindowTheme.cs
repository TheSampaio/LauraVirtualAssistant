using System.Runtime.InteropServices;
using Laura.App.Theming;
using Microsoft.UI.Windowing;

namespace Laura.App.Platforms.Windows;

/// <summary>Applies Laura's dark palette to native window chrome and caption buttons.</summary>
public static class WindowsWindowTheme
{
    /// <summary>Uses native title-bar theming without replacing minimize, maximize or close behavior.</summary>
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
    }
    /// <summary>Converts a shared MAUI color to the Windows ARGB title-bar value.</summary>
    private static global::Windows.UI.Color NativeColor(Color color) => global::Windows.UI.Color.FromArgb(255, (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255));
    /// <summary>Converts a shared color to the BGR value used by Desktop Window Manager.</summary>
    private static int ColorRef(Color color) => (int)(color.Red * 255) | ((int)(color.Green * 255) << 8) | ((int)(color.Blue * 255) << 16);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
}

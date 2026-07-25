using System.Runtime.InteropServices;

namespace Laura.App.Interop;

/// <summary>
/// Adjustments to native window chrome that Windows Forms does not expose.
/// </summary>
public static partial class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>
    /// Paints a window title bar in dark mode.
    ///
    /// Without this, the title bar would remain bright over a dark interface,
    /// breaking the impression of a unified theme.
    ///
    /// Args:
    ///     handle: Native window handle.
    ///     enabled: <see langword="true"/> para a barra escura.
    /// </summary>
    public static void UseDarkTitleBar(nint handle, bool enabled = true)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        int value = enabled ? 1 : 0;

        // Silently ignored on Windows versions before dark mode support.
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

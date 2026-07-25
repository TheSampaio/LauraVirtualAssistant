using System.Drawing;

namespace Laura.App.Theming;

/// <summary>
/// Palette and metrics for a modern dark theme for the settings window.
///
/// Windows Forms has no built-in theme system; centralizing colors and spacing
/// here keeps the interface cohesive and easy to adjust.
/// </summary>
internal static class Palette
{
    /// <summary>Main window background.</summary>
    internal static Color Background { get; } = Color.FromArgb(0x08, 0x09, 0x0C);

    /// <summary>Background for cards and raised panels.</summary>
    internal static Color Surface { get; } = Color.FromArgb(0x12, 0x14, 0x19);

    /// <summary>Input field background.</summary>
    internal static Color Field { get; } = Color.FromArgb(0x1A, 0x1D, 0x24);

    /// <summary>Accent color used for focus, selection, and primary actions.</summary>
    internal static Color Accent { get; } = Color.FromArgb(0xD9, 0x1F, 0x2E);

    /// <summary>Lighter accent variant for hover states.</summary>
    internal static Color AccentHover { get; } = Color.FromArgb(0xF0, 0x3A, 0x49);

    /// <summary>Primary text.</summary>
    internal static Color TextPrimary { get; } = Color.FromArgb(0xF5, 0xF7, 0xFA);

    /// <summary>Secondary text for hints and descriptions.</summary>
    internal static Color TextSecondary { get; } = Color.FromArgb(0xA2, 0xA7, 0xB3);

    /// <summary>Subtle dividers and outlines.</summary>
    internal static Color Border { get; } = Color.FromArgb(0x24, 0x28, 0x32);

    /// <summary>Dark chat scrollbar track.</summary>
    internal static Color ScrollTrack { get; } = Color.FromArgb(0x0D, 0x0F, 0x14);

    /// <summary>Dark chat scrollbar thumb.</summary>
    internal static Color ScrollThumb { get; } = Color.FromArgb(0x33, 0x38, 0x45);

    /// <summary>Active listening indicator color.</summary>
    internal static Color Listening { get; } = Color.FromArgb(0x38, 0xD9, 0x96);

    /// <summary>Speaking indicator color.</summary>
    internal static Color Speaking { get; } = Color.FromArgb(0x64, 0xB5, 0xF6);

    /// <summary>Idle indicator color.</summary>
    internal static Color Muted { get; } = Color.FromArgb(0x6C, 0x6C, 0x78);

    /// <summary>Warning color for conditions that need user attention.</summary>
    internal static Color Warning { get; } = Color.FromArgb(0xE8, 0xA3, 0x3D);

    /// <summary>Preferred font family, with fallback to an always-present font.</summary>
    internal const string FontFamily = "Segoe UI Variable Display";

    /// <summary>Fallback font family, available on every Windows installation.</summary>
    internal const string FallbackFontFamily = "Segoe UI";

    /// <summary>Base spacing, in pixels, from which other measurements derive.</summary>
    internal const int Unit = 8;
}

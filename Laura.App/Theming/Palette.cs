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
    internal static Color Background { get; } = Color.FromArgb(0x1B, 0x1B, 0x1F);

    /// <summary>Background for cards and raised panels.</summary>
    internal static Color Surface { get; } = Color.FromArgb(0x25, 0x25, 0x2B);

    /// <summary>Input field background.</summary>
    internal static Color Field { get; } = Color.FromArgb(0x2E, 0x2E, 0x35);

    /// <summary>Accent color used for focus, selection, and primary actions.</summary>
    internal static Color Accent { get; } = Color.FromArgb(0x7C, 0x5C, 0xFF);

    /// <summary>Lighter accent variant for hover states.</summary>
    internal static Color AccentHover { get; } = Color.FromArgb(0x8E, 0x72, 0xFF);

    /// <summary>Primary text.</summary>
    internal static Color TextPrimary { get; } = Color.FromArgb(0xF2, 0xF2, 0xF5);

    /// <summary>Secondary text for hints and descriptions.</summary>
    internal static Color TextSecondary { get; } = Color.FromArgb(0x9A, 0x9A, 0xA6);

    /// <summary>Subtle dividers and outlines.</summary>
    internal static Color Border { get; } = Color.FromArgb(0x3A, 0x3A, 0x42);

    /// <summary>Active listening indicator color.</summary>
    internal static Color Listening { get; } = Color.FromArgb(0x4C, 0xC2, 0x8C);

    /// <summary>Speaking indicator color.</summary>
    internal static Color Speaking { get; } = Color.FromArgb(0x5A, 0x9C, 0xFF);

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

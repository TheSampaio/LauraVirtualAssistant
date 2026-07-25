using System.Drawing;

namespace Laura.App.Theming;

/// <summary>
/// Creates fonts with automatic fallback to an always-present family.
///
/// The preferred family ("Segoe UI Variable") exists only on Windows 11; without the fallback,
/// the interface would appear with the generic system font on Windows 10.
/// </summary>
internal static class FontFactory
{
    /// <summary>
    /// Creates a font with the requested size and weight.
    ///
    /// Args:
    ///     size: Size in points.
    ///     style: Font style, such as bold.
    ///
    /// Returns:
    ///     A font in the preferred family, or the fallback when that family is unavailable.
    /// </summary>
    internal static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font(Palette.FontFamily, size, style, GraphicsUnit.Point);

        return font.Name.Equals(Palette.FontFamily, StringComparison.OrdinalIgnoreCase)
            ? font
            : new Font(Palette.FallbackFontFamily, size, style, GraphicsUnit.Point);
    }
}

using System.Drawing;

namespace Laura.App.Theming;

/// <summary>
/// Cria fontes com recuo automático para uma família sempre presente.
///
/// A família preferida ("Segoe UI Variable") só existe no Windows 11; sem o recuo,
/// a interface apareceria com a fonte genérica do sistema no Windows 10.
/// </summary>
internal static class FontFactory
{
    /// <summary>
    /// Cria uma fonte no tamanho e peso pedidos.
    ///
    /// Args:
    ///     size: Tamanho em pontos.
    ///     style: Estilo da fonte, como negrito.
    ///
    /// Returns:
    ///     Uma fonte na família preferida, ou na de recuo quando aquela não existe.
    /// </summary>
    internal static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font(Palette.FontFamily, size, style, GraphicsUnit.Point);

        return font.Name.Equals(Palette.FontFamily, StringComparison.OrdinalIgnoreCase)
            ? font
            : new Font(Palette.FallbackFontFamily, size, style, GraphicsUnit.Point);
    }
}

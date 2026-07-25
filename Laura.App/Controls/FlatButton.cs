using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Botão plano nos estilos primário e secundário do tema.
///
/// Dimensiona-se pelo próprio texto: com largura fixa, rótulos traduzidos apareciam
/// cortados ("Restaurar padrões" virava "Rest").
/// </summary>
public sealed class FlatButton : Button
{
    private bool _isPrimary = true;

    /// <summary>
    /// Inicializa o botão com aparência plana e dimensionamento automático.
    /// </summary>
    public FlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = FontFactory.Create(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, 38);
        Padding = new Padding(Palette.Unit * 2, Palette.Unit, Palette.Unit * 2, Palette.Unit);
        UseVisualStyleBackColor = false;

        ApplyPalette();
    }

    /// <summary>
    /// Obtém ou define se o botão usa a cor de destaque (ação primária).
    ///
    /// Botões secundários ficam sobre a cor de superfície, para não competir com a
    /// ação principal da janela.
    /// </summary>
    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            _isPrimary = value;
            ApplyPalette();
        }
    }

    /// <summary>
    /// Aplica as cores correspondentes ao papel do botão.
    /// </summary>
    private void ApplyPalette()
    {
        if (_isPrimary)
        {
            BackColor = Palette.Accent;
            FlatAppearance.MouseOverBackColor = Palette.AccentHover;
            FlatAppearance.MouseDownBackColor = Palette.Accent;
            ForeColor = Color.White;
        }
        else
        {
            BackColor = Palette.Surface;
            FlatAppearance.MouseOverBackColor = Palette.Field;
            FlatAppearance.MouseDownBackColor = Palette.Surface;
            ForeColor = Palette.TextPrimary;
        }
    }
}

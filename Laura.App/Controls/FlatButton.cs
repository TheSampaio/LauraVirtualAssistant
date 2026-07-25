using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Flat button in the theme primary and secondary styles.
///
/// Sizes itself by its own text: with fixed width, translated labels appeared
/// clipped ("Restore defaults" became "Rest").
/// </summary>
public sealed class FlatButton : Button
{
    private bool _isPrimary = true;

    /// <summary>
    /// Initializes the button with flat appearance and automatic sizing.
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
    /// Gets or sets whether the button uses the accent color (primary action).
    ///
    /// Secondary buttons sit on the surface color so they do not compete with the
    /// window's main action.
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
    /// Applies the colors corresponding to the button role.
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

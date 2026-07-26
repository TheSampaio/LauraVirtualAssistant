using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Round icon-style button for compact chat actions.
/// </summary>
internal sealed class CircleButton : Button
{
    private bool _isPrimary;

    /// <summary>
    /// Initializes the button.
    /// </summary>
    public CircleButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = FontFactory.CreateIcon(13f);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Size = new Size(44, 44);
        MinimumSize = new Size(44, 44);
        Padding = Padding.Empty;
        ApplyPalette();
    }

    /// <summary>Gets or sets whether the button uses the accent color.</summary>
    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            _isPrimary = value;
            ApplyPalette();
        }
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRegion();
    }

    /// <inheritdoc />
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ApplyPalette();
    }

    /// <inheritdoc />
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Region?.Dispose();
        base.OnHandleDestroyed(e);
    }

    private void ApplyPalette()
    {
        if (!Enabled)
        {
            BackColor = Palette.Surface;
            ForeColor = Palette.Muted;
            FlatAppearance.MouseOverBackColor = Palette.Surface;
            FlatAppearance.MouseDownBackColor = Palette.Surface;
            return;
        }

        BackColor = IsPrimary ? Palette.Accent : Palette.Field;
        ForeColor = Palette.TextPrimary;
        FlatAppearance.MouseOverBackColor = IsPrimary ? Palette.AccentHover : Palette.Surface;
        FlatAppearance.MouseDownBackColor = IsPrimary ? Palette.Accent : Palette.Field;
    }

    private void ApplyRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        int size = Math.Min(Width, Height);
        Region?.Dispose();
        using var path = new GraphicsPath();
        path.AddEllipse((Width - size) / 2, (Height - size) / 2, size, size);
        Region = new Region(path);
    }
}

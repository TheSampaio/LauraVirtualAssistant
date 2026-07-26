using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Side navigation item that switches between settings sections.
/// </summary>
public sealed class NavButton : Control
{
    private bool _selected;
    private bool _hovered;
    private readonly Font _iconFont = FontFactory.CreateIcon(12f);

    /// <summary>
    /// Initializes the navigation item.
    ///
    /// Args:
    ///     caption: Displayed label.
    ///     glyph: Native Windows icon that precedes the label.
    /// </summary>
    public NavButton(string caption, string glyph)
    {
        ArgumentNullException.ThrowIfNull(caption);

        Caption = caption;
        Glyph = glyph;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Height = 42;
        Cursor = Cursors.Hand;
        Font = FontFactory.Create(10f);
    }

    /// <summary>Gets the section label.</summary>
    public string Caption { get; }

    /// <summary>Gets the symbol displayed before the label.</summary>
    public string Glyph { get; }

    /// <summary>Gets or sets whether this is the selected item.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color background = _selected ? Palette.Field : _hovered ? Palette.Surface : Palette.Background;
        using (var backgroundBrush = new SolidBrush(background))
        using (GraphicsPath shape = CreateRoundedRectangle(new Rectangle(0, 2, Width - 1, Height - 4), 8))
        {
            graphics.FillPath(backgroundBrush, shape);
        }

        if (_selected)
        {
            using var indicatorBrush = new SolidBrush(Palette.Accent);
            graphics.FillRectangle(indicatorBrush, 0, Height / 2 - 8, 3, 16);
        }

        Color textColor = _selected ? Palette.TextPrimary : Palette.TextSecondary;
        var iconBounds = new Rectangle(Palette.Unit * 2, 0, 24, Height);
        var textBounds = new Rectangle(Palette.Unit * 6, 0, Width - Palette.Unit * 7, Height);
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix;

        TextRenderer.DrawText(graphics, Glyph, _iconFont, iconBounds, textColor, flags);
        TextRenderer.DrawText(graphics, Caption, Font, textBounds, textColor, flags);
    }

    /// <summary>
    /// Creates a rounded rectangle.
    ///
    /// Args:
    ///     bounds: Rectangle area.
    ///     radius: Corner radius.
    ///
    /// Returns:
    ///     The corresponding path.
    /// </summary>
    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

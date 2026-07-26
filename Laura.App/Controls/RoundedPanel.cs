using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Laura.App.Controls;

/// <summary>
/// Panel with a rounded clipping region.
/// </summary>
internal sealed class RoundedPanel : Panel
{
    private int _cornerRadius = 10;
    private Color _borderColor = Color.Transparent;
    private int _borderThickness;

    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            ApplyRegion();
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    public int BorderThickness
    {
        get => _borderThickness;
        set
        {
            _borderThickness = Math.Max(0, value);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (BorderThickness <= 0 || BorderColor == Color.Transparent)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle bounds = new(
            BorderThickness / 2,
            BorderThickness / 2,
            Width - BorderThickness,
            Height - BorderThickness);

        using GraphicsPath shape = CreateRoundedRectangle(bounds, CornerRadius);
        using var pen = new Pen(BorderColor, BorderThickness);
        e.Graphics.DrawPath(pen, shape);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRegion();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Region?.Dispose();
        base.OnHandleDestroyed(e);
    }

    private void ApplyRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        Region?.Dispose();
        Region = new Region(CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius));
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

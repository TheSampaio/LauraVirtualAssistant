using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Chat transcript surface with a quiet pattern behind the messages.
/// </summary>
internal sealed class ChatSurfacePanel : Panel
{
    /// <summary>
    /// Initializes the chat surface.
    /// </summary>
    public ChatSurfacePanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Palette.ChatBackground;
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new SolidBrush(Palette.ChatBackground);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(Color.FromArgb(18, Palette.TextSecondary), 1f);

        const int cell = 74;
        for (int y = 10; y < Height; y += cell)
        {
            for (int x = 8; x < Width; x += cell)
            {
                DrawPatternItem(graphics, pen, x, y, (x / cell + y / cell) % 4);
            }
        }
    }

    private static void DrawPatternItem(Graphics graphics, Pen pen, int x, int y, int kind)
    {
        switch (kind)
        {
            case 0:
                DrawRoundedRectangle(graphics, pen, new Rectangle(x, y, 34, 24), 7);
                graphics.DrawLine(pen, x + 12, y + 24, x + 7, y + 31);
                break;

            case 1:
                graphics.DrawEllipse(pen, x + 4, y + 2, 28, 28);
                graphics.DrawArc(pen, x + 10, y + 9, 16, 14, 210, 120);
                break;

            case 2:
                DrawRoundedRectangle(graphics, pen, new Rectangle(x + 3, y + 2, 30, 30), 6);
                graphics.DrawLine(pen, x + 11, y + 12, x + 25, y + 12);
                graphics.DrawLine(pen, x + 11, y + 20, x + 21, y + 20);
                break;

            default:
                graphics.DrawEllipse(pen, x + 6, y + 6, 8, 8);
                graphics.DrawEllipse(pen, x + 18, y + 6, 8, 8);
                graphics.DrawEllipse(pen, x + 30, y + 6, 8, 8);
                break;
        }
    }

    private static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        using var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        graphics.DrawPath(pen, path);
    }
}

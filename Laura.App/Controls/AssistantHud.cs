using System.Drawing.Drawing2D;
using Laura.App.Theming;
using Laura.Core.Engine;

namespace Laura.App.Controls;

/// <summary>Laura's independently drawn status display; rings indicate engine activity, not audio amplitude.</summary>
internal sealed class AssistantHud : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 80 };
    private readonly Font _captionFont = FontFactory.Create(9f);
    private readonly Font _statusFont = FontFactory.Create(11f, FontStyle.Bold);
    private AssistantState _state;
    private float _phase;
    private string _stateText = string.Empty;
    private bool _recording;

    internal AssistantHud()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Background;
        _animation.Tick += (_, _) => { _phase = (_phase + 3) % 360; Invalidate(); };
    }

    internal AssistantState State
    {
        get => _state;
        set { _state = value; UpdateAnimation(); Invalidate(); }
    }

    internal string StateText
    {
        get => _stateText;
        set { if (_stateText == value) { return; } _stateText = value; Invalidate(); }
    }

    internal string Caption { get; set; } = string.Empty;

    internal bool Recording
    {
        get => _recording;
        set { _recording = value; UpdateAnimation(); Invalidate(); }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateAnimation();
    }

    private void UpdateAnimation() => _animation.Enabled = Visible && (_recording || _state is AssistantState.Working or AssistantState.Speaking);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Max(80, Math.Min(Width - 50, Height - 110));
        var ring = new RectangleF((Width - size) / 2, (Height - size) / 2 - 14, size, size);
        float cx = Width / 2f;
        float cy = ring.Top + size / 2;
        using var grid = new Pen(Color.FromArgb(25, Palette.Accent), 1);
        for (int x = 0; x < Width; x += 32) { graphics.DrawLine(grid, x, 0, x, Height); }
        for (int y = 0; y < Height; y += 32) { graphics.DrawLine(grid, 0, y, Width, y); }
        using var dim = new Pen(Color.FromArgb(65, Palette.Accent), 1);
        using var accent = new Pen(Palette.AccentHover, 2);
        using var soft = new Pen(Color.FromArgb(135, Palette.Accent), 5);
        using var fill = new SolidBrush(Color.FromArgb(20, Palette.Accent));
        graphics.FillEllipse(fill, ring);
        graphics.DrawEllipse(dim, ring);
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI / 36;
            float outer = size / 2;
            float inner = outer - (i % 6 == 0 ? 13 : 5);
            graphics.DrawLine(i % 6 == 0 ? accent : dim,
                cx + inner * (float)Math.Cos(angle), cy + inner * (float)Math.Sin(angle),
                cx + outer * (float)Math.Cos(angle), cy + outer * (float)Math.Sin(angle));
        }
        ring.Inflate(-24, -24);
        for (int i = 0; i < 4; i++) { graphics.DrawArc(accent, ring, _phase + i * 90 + 8, 64); }
        ring.Inflate(-15, -15);
        graphics.DrawArc(soft, ring, -_phase + 15, 125);
        graphics.DrawArc(soft, ring, -_phase + 195, 125);
        ring.Inflate(-14, -14);
        graphics.DrawEllipse(dim, ring);
        DrawHologram(graphics, cx, cy, size);
        TextRenderer.DrawText(graphics, "L A U R A", _captionFont,
            new Rectangle(0, (int)(cy + size * 0.33f), Width, 24), Palette.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, StateText, _statusFont,
            new Rectangle(0, (int)(cy + size / 2) + 20, Width, 28), Palette.AccentHover,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, Caption, _captionFont,
            new Rectangle(0, (int)(cy + size / 2) + 50, Width, 24), Palette.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Draws an original geometric face from normalized curves; no upstream mesh or asset is used.</summary>
    private void DrawHologram(Graphics graphics, float cx, float cy, float scale)
    {
        GraphicsState saved = graphics.Save();
        graphics.TranslateTransform(cx, cy);
        graphics.ScaleTransform(scale, scale);
        using var edge = new Pen(Color.FromArgb(210, Palette.AccentHover), 0.004f);
        using var mesh = new Pen(Color.FromArgb(90, Palette.Accent), 0.002f);
        using var face = new GraphicsPath();
        face.AddBezier(-0.15f, -0.08f, -0.20f, -0.35f, 0.20f, -0.35f, 0.15f, -0.08f);
        face.AddBezier(0.15f, -0.08f, 0.14f, 0.10f, 0.09f, 0.23f, 0, 0.26f);
        face.AddBezier(0, 0.26f, -0.09f, 0.23f, -0.14f, 0.10f, -0.15f, -0.08f);
        face.CloseFigure();
        graphics.DrawPath(edge, face);
        graphics.SetClip(face, CombineMode.Intersect);
        for (int index = -5; index <= 5; index++)
        {
            float y = index * 0.048f;
            graphics.DrawBezier(mesh, -0.19f, y, -0.07f, y + 0.025f, 0.07f, y + 0.025f, 0.19f, y);
            float x = index * 0.034f;
            graphics.DrawBezier(mesh, x, -0.31f, x * 0.8f, -0.10f, x * 1.2f, 0.07f, x * 0.5f, 0.26f);
        }
        graphics.DrawBezier(edge, -0.12f, -0.061f, -0.086f, -0.091f, -0.045f, -0.085f, -0.028f, -0.06f);
        graphics.DrawBezier(edge, 0.12f, -0.061f, 0.086f, -0.091f, 0.045f, -0.085f, 0.028f, -0.06f);
        graphics.DrawBezier(edge, -0.11f, -0.034f, -0.08f, -0.055f, -0.053f, -0.055f, -0.036f, -0.032f);
        graphics.DrawBezier(edge, 0.11f, -0.034f, 0.08f, -0.055f, 0.053f, -0.055f, 0.036f, -0.032f);
        graphics.DrawLine(edge, -0.019f, -0.02f, -0.031f, 0.076f);
        graphics.DrawLine(edge, 0.019f, -0.02f, 0.031f, 0.076f);
        graphics.DrawBezier(edge, -0.031f, 0.076f, -0.01f, 0.093f, 0.01f, 0.093f, 0.031f, 0.076f);
        float mouth = _state is AssistantState.Speaking ? 0.015f + 0.012f * Math.Abs((float)Math.Sin(_phase * Math.PI / 90)) : 0.006f;
        graphics.DrawBezier(edge, -0.063f, 0.145f, -0.022f, 0.13f - mouth, 0.022f, 0.13f - mouth, 0.063f, 0.145f);
        graphics.DrawBezier(edge, -0.063f, 0.145f, -0.022f, 0.15f + mouth, 0.022f, 0.15f + mouth, 0.063f, 0.145f);
        graphics.Restore(saved);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose(); _captionFont.Dispose(); _statusFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

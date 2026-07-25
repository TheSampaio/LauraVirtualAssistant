using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// On/off switch, more direct for binary options than a checkbox.
///
/// Like <see cref="Slider"/>, paints over an opaque background to avoid flicker.
/// </summary>
public sealed class ToggleSwitch : Control
{
    private const int TrackWidth = 44;
    private const int TrackHeight = 24;
    private const int KnobInset = 3;

    private bool _isOn;

    /// <summary>
    /// Initializes the switch with smooth rendering.
    /// </summary>
    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        BackColor = Palette.Surface;
        Size = new Size(TrackWidth, TrackHeight);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>
    /// Occurs when the state changes by click or assignment.
    /// </summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Gets or sets whether the switch is on.</summary>
    public bool Checked
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }

            _isOn = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Checked = !Checked;
    }

    /// <inheritdoc />
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space || base.IsInputKey(keyData);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackBounds = new Rectangle(0, (Height - TrackHeight) / 2, TrackWidth - 1, TrackHeight - 1);

        using (var trackBrush = new SolidBrush(_isOn ? Palette.Accent : Palette.Field))
        using (GraphicsPath trackPath = CreateCapsule(trackBounds))
        {
            graphics.FillPath(trackBrush, trackPath);

            using var borderPen = new Pen(_isOn ? Palette.AccentHover : Palette.Border);
            graphics.DrawPath(borderPen, trackPath);
        }

        int diameter = TrackHeight - KnobInset * 2;
        int knobX = _isOn ? TrackWidth - diameter - KnobInset : KnobInset;
        var knobBounds = new Rectangle(knobX, (Height - diameter) / 2, diameter, diameter);

        using var knobBrush = new SolidBrush(_isOn ? Color.White : Palette.TextSecondary);
        graphics.FillEllipse(knobBrush, knobBounds);
    }

    /// <summary>
    /// Cria o contorno arredondado do trilho.
    ///
    /// Args:
    ///     bounds: Track rectangle.
    ///
    /// Returns:
    ///     A capsule-shaped path.
    /// </summary>
    private static GraphicsPath CreateCapsule(Rectangle bounds)
    {
        int radius = bounds.Height;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, radius, radius, 90, 180);
        path.AddArc(bounds.Right - radius, bounds.Y, radius, radius, 270, 180);
        path.CloseFigure();

        return path;
    }
}

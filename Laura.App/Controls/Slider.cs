using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Slider drawn from scratch for voice rate, pitch, and volume.
///
/// The standard <see cref="TrackBar"/> does not accept theme colors. The background is deliberately opaque:
/// transparent backgrounds in Windows Forms are emulated by repainting the parent on every
/// frame, which causes visible flicker while dragging.
/// </summary>
public sealed class Slider : Control
{
    private const int TrackHeight = 4;
    private const int ThumbRadius = 8;

    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;

    /// <summary>
    /// Initializes the control with smooth, flicker-free rendering.
    /// </summary>
    public Slider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        BackColor = Palette.Surface;
        Height = ThumbRadius * 2 + 6;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>
    /// Occurs when the value changes by user interaction or assignment.
    /// </summary>
    public event EventHandler? ValueChanged;

    /// <summary>Gets or sets the smallest range value.</summary>
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Value = _value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the largest range value.</summary>
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            Value = _value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the current value, always within range.</summary>
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);

            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button is MouseButtons.Left)
        {
            _dragging = true;
            Focus();
            Value = ValueFromPosition(e.X);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            Value = ValueFromPosition(e.X);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta);
    }

    /// <inheritdoc />
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down:
                Value -= 1;
                e.Handled = true;
                break;

            case Keys.Right or Keys.Up:
                Value += 1;
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int centerY = Height / 2;
        int trackLeft = ThumbRadius;
        int trackWidth = Math.Max(1, Width - ThumbRadius * 2);
        int thumbX = trackLeft + (int)(Fraction() * trackWidth);

        using (var trackBrush = new SolidBrush(Palette.Border))
        {
            graphics.FillRectangle(trackBrush, trackLeft, centerY - TrackHeight / 2, trackWidth, TrackHeight);
        }

        using (var fillBrush = new SolidBrush(Palette.Accent))
        {
            graphics.FillRectangle(fillBrush, trackLeft, centerY - TrackHeight / 2, thumbX - trackLeft, TrackHeight);
        }

        var thumbBounds = new Rectangle(thumbX - ThumbRadius, centerY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);

        using (var thumbBrush = new SolidBrush(Palette.TextPrimary))
        {
            graphics.FillEllipse(thumbBrush, thumbBounds);
        }

        using var ringPen = new Pen(Focused ? Palette.AccentHover : Palette.Accent, 2);
        graphics.DrawEllipse(ringPen, thumbBounds);
    }

    /// <summary>
    /// Calculates the filled track fraction for the current value.
    ///
    /// Returns:
    ///     A number from 0.0 to 1.0.
    /// </summary>
    private float Fraction()
    {
        int range = _maximum - _minimum;
        return range <= 0 ? 0f : (float)(_value - _minimum) / range;
    }

    /// <summary>
    /// Converts a horizontal coordinate to the corresponding range value.
    ///
    /// Args:
    ///     x: Pixel coordinate within the control.
    ///
    /// Returns:
    ///     The range value closest to the position.
    /// </summary>
    private int ValueFromPosition(int x)
    {
        int trackWidth = Math.Max(1, Width - ThumbRadius * 2);
        float fraction = Math.Clamp((float)(x - ThumbRadius) / trackWidth, 0f, 1f);

        return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
    }
}

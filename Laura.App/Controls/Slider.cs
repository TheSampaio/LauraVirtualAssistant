using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Controle deslizante desenhado do zero, para velocidade, tom e volume da voz.
///
/// A <see cref="TrackBar"/> padrão não aceita as cores do tema. O fundo é opaco de
/// propósito: fundo transparente no Windows Forms é emulado repintando o pai a cada
/// quadro, o que provoca cintilação visível ao arrastar.
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
    /// Inicializa o controle com renderização suave e sem cintilação.
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
    /// Ocorre quando o valor muda por interação do usuário ou por atribuição.
    /// </summary>
    public event EventHandler? ValueChanged;

    /// <summary>Obtém ou define o menor valor da faixa.</summary>
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

    /// <summary>Obtém ou define o maior valor da faixa.</summary>
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

    /// <summary>Obtém ou define o valor atual, sempre dentro da faixa.</summary>
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
    /// Calcula a fração preenchida da trilha para o valor atual.
    ///
    /// Returns:
    ///     Um número de 0.0 a 1.0.
    /// </summary>
    private float Fraction()
    {
        int range = _maximum - _minimum;
        return range <= 0 ? 0f : (float)(_value - _minimum) / range;
    }

    /// <summary>
    /// Converte uma coordenada horizontal no valor correspondente da faixa.
    ///
    /// Args:
    ///     x: Coordenada em pixels dentro do controle.
    ///
    /// Returns:
    ///     O valor da faixa mais próximo da posição.
    /// </summary>
    private int ValueFromPosition(int x)
    {
        int trackWidth = Math.Max(1, Width - ThumbRadius * 2);
        float fraction = Math.Clamp((float)(x - ThumbRadius) / trackWidth, 0f, 1f);

        return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
    }
}

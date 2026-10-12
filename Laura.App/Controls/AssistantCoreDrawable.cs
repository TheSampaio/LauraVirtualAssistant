using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>Draws concentric counter-rotating reactor rings with no face or raster asset.</summary>
public sealed class AssistantCoreDrawable : IDrawable
{
    /// <summary>Gets the common period for ring speeds 1, 1.3, .7 and .35 and both pulse periods.</summary>
    public const float CycleDegrees = 7200;
    /// <summary>Gets or sets the animation angle in degrees.</summary>
    public float Angle { get; set; }
    /// <summary>Gets or sets whether the core is speaking or processing.</summary>
    public bool Active => Mode != ReactorMode.Idle;
    /// <summary>Gets or sets the audible or listening visual state.</summary>
    public ReactorMode Mode { get; set; }
    /// <summary>Gets or sets the smoothed current audio level.</summary>
    public float Level { get; set; }
    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        Color accent = Mode == ReactorMode.Listening ? Theme.Listening : Mode == ReactorMode.Speaking ? Theme.Speaking : Theme.Accent;
        float cx = dirtyRect.Center.X, cy = dirtyRect.Center.Y;
        float radius = MathF.Min(dirtyRect.Width, dirtyRect.Height) * .39f;
        if (radius <= 0) { return; }
        radius *= Active ? 1 + MathF.Sin(Angle * MathF.PI / 45) * .012f + Level * .025f : 1;
        canvas.StrokeColor = Color.FromArgb("#251420"); canvas.StrokeSize = 1;
        for (float x = 0; x < dirtyRect.Width; x += 40) { canvas.DrawLine(x, 0, x, dirtyRect.Height); }
        for (float y = 0; y < dirtyRect.Height; y += 40) { canvas.DrawLine(0, y, dirtyRect.Width, y); }
        for (int glow = 18; glow > 0; glow--)
        {
            canvas.FillColor = accent.WithAlpha(Active ? .018f + Level * .012f : .008f);
            canvas.FillCircle(cx, cy, radius + glow * 2);
        }
        canvas.StrokeColor = accent.WithAlpha(.28f); canvas.StrokeSize = 1;
        canvas.DrawCircle(cx, cy, radius); canvas.DrawCircle(cx, cy, radius * .94f);
        Ring(canvas, cx, cy, radius * .89f, Angle, 3, 84, 2.5f, accent);
        Ring(canvas, cx, cy, radius * .73f, -Angle * 1.3f + 40, 3, 90, 11, accent);
        Ring(canvas, cx, cy, radius * .58f, Angle * .7f, 2, 145, 2, accent);
        canvas.StrokeColor = accent.WithAlpha(.65f);
        for (int index = 0; index < 90; index++)
        {
            float theta = (index * 4 - Angle * .35f) * MathF.PI / 180;
            float outer = radius * .81f, inner = radius * (index % 5 == 0 ? .775f : .797f);
            canvas.StrokeSize = index % 5 == 0 ? 2 : 1;
            canvas.DrawLine(cx + MathF.Cos(theta) * inner, cy + MathF.Sin(theta) * inner,
                cx + MathF.Cos(theta) * outer, cy + MathF.Sin(theta) * outer);
        }
        float pulse = Active ? 1 + MathF.Sin(Angle * MathF.PI / 90) * .035f : 1;
        canvas.FillColor = Color.FromArgb("#27101B"); canvas.FillCircle(cx, cy, radius * .43f * pulse);
        canvas.StrokeColor = accent.WithAlpha(.8f); canvas.StrokeSize = 1.5f; canvas.DrawCircle(cx, cy, radius * .43f * pulse);
        for (int index = 0; index < 25; index++)
        {
            float theta = (index * 3 + Angle) * MathF.PI / 180;
            canvas.DrawLine(cx + MathF.Cos(theta) * radius * .39f, cy + MathF.Sin(theta) * radius * .39f,
                cx + MathF.Cos(theta) * radius * .35f, cy + MathF.Sin(theta) * radius * .35f);
        }
        canvas.FontColor = Theme.Text; canvas.FontSize = 15;
        canvas.DrawString("L A U R A", cx - radius / 2, cy - 12, radius, 28, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.FontColor = accent; canvas.FontSize = 9;
        canvas.DrawString("PERSONAL AI", cx - radius / 2, cy + 14, radius, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.StrokeColor = accent; canvas.StrokeSize = 1;
        for (int i = 0; i < 4; i++)
        { float theta = i * MathF.PI / 2; canvas.DrawLine(cx + MathF.Cos(theta) * radius * 1.02f, cy + MathF.Sin(theta) * radius * 1.02f, cx + MathF.Cos(theta) * radius * 1.08f, cy + MathF.Sin(theta) * radius * 1.08f); }
    }
    /// <summary>Draws separated arcs around a ring using geometry shared by every canvas backend.</summary>
    private static void Ring(ICanvas canvas, float cx, float cy, float radius, float angle, int segments, float sweep, float width, Color accent)
    {
        canvas.StrokeColor = accent; canvas.StrokeSize = width; canvas.StrokeLineCap = LineCap.Round;
        for (int segment = 0; segment < segments; segment++)
        {
            var path = new PathF();
            for (int step = 0; step <= (int)sweep; step += 2)
            {
                float theta = (angle + segment * 360f / segments + step) * MathF.PI / 180;
                float x = cx + MathF.Cos(theta) * radius, y = cy + MathF.Sin(theta) * radius;
                if (step == 0) { path.MoveTo(x, y); } else { path.LineTo(x, y); }
            }
            canvas.DrawPath(path);
        }
    }
}

/// <summary>Distinguishes microphone input from native speech output.</summary>
public enum ReactorMode
{
    /// <summary>No audio capture or playback.</summary>
    Idle,
    /// <summary>Active microphone capture.</summary>
    Listening,
    /// <summary>Native voice playback.</summary>
    Speaking,
    /// <summary>Processing a request.</summary>
    Working,
    /// <summary>Preparing a generated response or verified wake briefing.</summary>
    Thinking,
}

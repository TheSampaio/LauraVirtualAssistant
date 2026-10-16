using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>Draws concentric counter-rotating reactor rings with no face or raster asset.</summary>
public sealed class AssistantCoreDrawable : IDrawable
{
    private static readonly Color GridColor = Color.FromArgb("#251420"), CenterColor = Color.FromArgb("#27101B");
    private static readonly PathF[] OuterArcs = CreateArcs(3, 84), MiddleArcs = CreateArcs(3, 90), InnerArcs = CreateArcs(2, 145);
    private float _activity, _thinking, _speaking, _listening;
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
    /// <summary>Gets the elapsed-time-smoothed activity amount for continuous angular speed.</summary>
    public float ActivityAmount => _activity;
    /// <summary>Gets the processing marker's current transition amount.</summary>
    public float ThinkingAmount => _thinking;
    /// <summary>Gets the current speech-color transition amount.</summary>
    public float SpeakingAmount => _speaking;
    /// <summary>Advances visual transitions independently of inference speed. Args: elapsedSeconds: Measured frame interval; animate: Whether Windows permits motion.</summary>
    public void Advance(float elapsedSeconds, bool animate)
    {
        float blend = animate ? 1 - MathF.Exp(-Math.Clamp(elapsedSeconds, 0, .1f) / .11f) : 1;
        _activity += ((Active ? 1 : 0) - _activity) * blend;
        _thinking += ((Mode == ReactorMode.Thinking ? 1 : 0) - _thinking) * blend;
        _speaking += ((Mode == ReactorMode.Speaking ? 1 : 0) - _speaking) * blend;
        _listening += ((Mode == ReactorMode.Listening ? 1 : 0) - _listening) * blend;
    }
    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        Color accent = Blend(Blend(Theme.Accent, Theme.Speaking, _speaking), Theme.Listening, _listening);
        float cx = dirtyRect.Center.X, cy = dirtyRect.Center.Y;
        float radius = MathF.Min(dirtyRect.Width, dirtyRect.Height) * .39f;
        if (radius <= 0) { return; }
        radius *= 1 + MathF.Sin(Angle * MathF.PI / 45) * .012f * _activity + Level * .025f;
        canvas.StrokeColor = GridColor; canvas.StrokeSize = 1;
        for (float x = 0; x < dirtyRect.Width; x += 40) { canvas.DrawLine(x, 0, x, dirtyRect.Height); }
        for (float y = 0; y < dirtyRect.Height; y += 40) { canvas.DrawLine(0, y, dirtyRect.Width, y); }
        canvas.FillColor = accent.WithAlpha(.008f + _activity * .010f + Level * .012f);
        for (int glow = 18; glow > 0; glow--)
        {
            canvas.FillCircle(cx, cy, radius + glow * 2);
        }
        canvas.StrokeColor = accent.WithAlpha(.28f); canvas.StrokeSize = 1;
        canvas.DrawCircle(cx, cy, radius); canvas.DrawCircle(cx, cy, radius * .94f);
        Ring(canvas, cx, cy, radius * .89f, Angle, OuterArcs, 2.5f, accent);
        Ring(canvas, cx, cy, radius * .73f, -Angle * 1.3f + 40, MiddleArcs, 11, accent);
        Ring(canvas, cx, cy, radius * .58f, Angle * .7f, InnerArcs, 2, accent);
        canvas.StrokeColor = accent.WithAlpha(.65f);
        for (int index = 0; index < 90; index++)
        {
            float theta = (index * 4 - Angle * .35f) * MathF.PI / 180;
            float outer = radius * .81f, inner = radius * (index % 5 == 0 ? .775f : .797f);
            canvas.StrokeSize = index % 5 == 0 ? 2 : 1;
            canvas.DrawLine(cx + MathF.Cos(theta) * inner, cy + MathF.Sin(theta) * inner,
                cx + MathF.Cos(theta) * outer, cy + MathF.Sin(theta) * outer);
        }
        float pulse = 1 + MathF.Sin(Angle * MathF.PI / 90) * .035f * _activity;
        canvas.FillColor = CenterColor; canvas.FillCircle(cx, cy, radius * .43f * pulse);
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
        if (_thinking > .005f)
        {
            // Three phased dots and an orbit make processing distinct from idle/listening.
            for (int dot = 0; dot < 3; dot++)
            {
                float wave = (1 + MathF.Sin((Angle * 4 - dot * 120) * MathF.PI / 180)) / 2;
                canvas.FillColor = accent.WithAlpha(_thinking * (.3f + wave * .7f));
                canvas.FillCircle(cx + (dot - 1) * radius * .11f, cy + radius * .29f, radius * (.018f + wave * .009f));
            }
            for (int trail = 5; trail >= 0; trail--)
            {
                float theta = (Angle * 2 - trail * 10) * MathF.PI / 180;
                canvas.FillColor = accent.WithAlpha(_thinking * (1 - trail * .14f));
                canvas.FillCircle(cx + MathF.Cos(theta) * radius * .49f, cy + MathF.Sin(theta) * radius * .49f, radius * .018f);
            }
        }
        canvas.StrokeColor = accent; canvas.StrokeSize = 1;
        for (int i = 0; i < 4; i++)
        { float theta = i * MathF.PI / 2; canvas.DrawLine(cx + MathF.Cos(theta) * radius * 1.02f, cy + MathF.Sin(theta) * radius * 1.02f, cx + MathF.Cos(theta) * radius * 1.08f, cy + MathF.Sin(theta) * radius * 1.08f); }
    }
    /// <summary>Transforms retained unit-circle paths rather than allocating hundreds of new points on every frame.</summary>
    private static void Ring(ICanvas canvas, float cx, float cy, float radius, float angle, PathF[] arcs, float width, Color accent)
    {
        canvas.SaveState(); canvas.Translate(cx, cy); canvas.Rotate(angle); canvas.Scale(radius, radius);
        canvas.StrokeColor = accent; canvas.StrokeSize = width / radius; canvas.StrokeLineCap = LineCap.Round;
        foreach (PathF path in arcs) { canvas.DrawPath(path); }
        canvas.RestoreState();
    }
    /// <summary>Builds immutable shared arc geometry once, preserving the original ring sweeps and spacing.</summary>
    private static PathF[] CreateArcs(int segments, float sweep)
    {
        var arcs = new PathF[segments];
        for (int segment = 0; segment < segments; segment++)
        {
            var path = new PathF();
            for (int step = 0; step <= (int)sweep; step += 2)
            {
                float theta = (segment * 360f / segments + step) * MathF.PI / 180;
                float x = MathF.Cos(theta), y = MathF.Sin(theta);
                if (step == 0) { path.MoveTo(x, y); } else { path.LineTo(x, y); }
            }
            arcs[segment] = path;
        }
        return arcs;
    }
    /// <summary>Blends the existing state colors without replacing Laura's palette.</summary>
    private static Color Blend(Color from, Color to, float amount) => new(from.Red + (to.Red - from.Red) * amount,
        from.Green + (to.Green - from.Green) * amount, from.Blue + (to.Blue - from.Blue) * amount);
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

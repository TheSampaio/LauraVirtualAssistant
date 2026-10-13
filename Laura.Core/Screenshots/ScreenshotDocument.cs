using System.Collections.Immutable;

namespace Laura.Core.Screenshots;

/// <summary>Physical pixel coordinates independent of monitor DPI and editor zoom.</summary>
public readonly record struct ScreenshotPoint(double X, double Y);
/// <summary>A physical rectangle, including negative virtual-desktop origins.</summary>
public readonly record struct ScreenshotBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Clamps a physical selection to the original captured desktop.</summary>
    public ScreenshotBounds Intersect(ScreenshotBounds other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(checked(X + Width), checked(other.X + other.Width)), bottom = Math.Min(checked(Y + Height), checked(other.Y + other.Height));
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
    /// <summary>Normalizes a drag in either direction using physical endpoints.</summary>
    public static ScreenshotBounds Between(ScreenshotPoint first, ScreenshotPoint second) => new((int)Math.Floor(Math.Min(first.X, second.X)),
        (int)Math.Floor(Math.Min(first.Y, second.Y)), (int)Math.Ceiling(Math.Abs(second.X - first.X)), (int)Math.Ceiling(Math.Abs(second.Y - first.Y)));
}
/// <summary>Editable annotation kinds; erasing removes annotations, never original pixels.</summary>
public enum ScreenshotTool
{
    /// <summary>Select, move or resize an annotation.</summary>
    Select,
    /// <summary>Draw a rectangle.</summary>
    Rectangle,
    /// <summary>Draw an ellipse.</summary>
    Ellipse,
    /// <summary>Draw a line.</summary>
    Line,
    /// <summary>Draw an arrow.</summary>
    Arrow,
    /// <summary>Draw a freehand stroke.</summary>
    Pen,
    /// <summary>Add text.</summary>
    Text,
    /// <summary>Draw a translucent highlight.</summary>
    Highlight,
    /// <summary>Remove an annotation hit by the pointer.</summary>
    Eraser
}
/// <summary>Immutable original BGRA pixels. A capture is never saved or transmitted by this type.</summary>
public sealed class ScreenshotFrame : IDisposable
{
    private byte[]? _pixels;
    /// <summary>Validates a bounded physical frame and takes ownership of its pixels.</summary>
    public ScreenshotFrame(ScreenshotBounds bounds, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 32000000 || pixels.Length != checked(bounds.Width * bounds.Height * 4))
        { throw new ArgumentException("Invalid or oversized screenshot frame.", nameof(pixels)); }
        Bounds = bounds; _pixels = pixels;
    }
    /// <summary>Gets physical desktop geometry; editor annotations use coordinates relative to its origin.</summary>
    public ScreenshotBounds Bounds { get; }
    /// <summary>Gets immutable pixels until disposal.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels ?? throw new ObjectDisposedException(nameof(ScreenshotFrame));
    /// <summary>Crops original pixels without altering the source or following monitor DPI transforms.</summary>
    public ScreenshotFrame Crop(ScreenshotBounds region)
    {
        ScreenshotBounds selected = Bounds.Intersect(region);
        if (selected.Width == 0 || selected.Height == 0) { throw new ArgumentException("Empty selection.", nameof(region)); }
        byte[] cropped = new byte[checked(selected.Width * selected.Height * 4)];
        for (int row = 0; row < selected.Height; row++)
        { Pixels.Span.Slice(checked(((selected.Y - Bounds.Y + row) * Bounds.Width + selected.X - Bounds.X) * 4), selected.Width * 4).CopyTo(cropped.AsSpan(row * selected.Width * 4)); }
        return new(selected, cropped);
    }
    /// <inheritdoc />
    public void Dispose() { if (_pixels is { } pixels) { Array.Clear(pixels); _pixels = null; } }
}
/// <summary>One immutable editable annotation in original-image coordinates.</summary>
public sealed record ScreenshotAnnotation(Guid Id, ScreenshotTool Tool, ImmutableArray<ScreenshotPoint> Points, uint Color, double Thickness, string Text = "", double FontSize = 24)
{
    /// <summary>Moves an annotation while preserving style and identity.</summary>
    public ScreenshotAnnotation Move(double x, double y) => this with { Points = Points.Select(point => new ScreenshotPoint(point.X + x, point.Y + y)).ToImmutableArray() };
    /// <summary>Gets a hit/resize box; text uses a conservative box until rendered by the native font engine.</summary>
    public (double X, double Y, double Width, double Height) Box()
    {
        double x = Points.Min(point => point.X), y = Points.Min(point => point.Y);
        return Tool == ScreenshotTool.Text ? (x, y, Math.Max(FontSize, Text.Length * FontSize * .65), FontSize * 1.5)
            : (x, y, Math.Max(1, Points.Max(point => point.X) - x), Math.Max(1, Points.Max(point => point.Y) - y));
    }
    /// <summary>Scales an annotation from its original top-left rather than changing original image pixels.</summary>
    public ScreenshotAnnotation Resize(double width, double height)
    {
        var box = Box(); double sx = Math.Max(4, width) / box.Width, sy = Math.Max(4, height) / box.Height;
        return this with { Points = Points.Select(point => new ScreenshotPoint(box.X + (point.X - box.X) * sx, box.Y + (point.Y - box.Y) * sy)).ToImmutableArray(), FontSize = Tool == ScreenshotTool.Text ? Math.Clamp(FontSize * sy, 8, 240) : FontSize };
    }
}
/// <summary>Non-destructive bounded document history shared by editor rendering and export.</summary>
public sealed class ScreenshotDocument(ScreenshotFrame frame) : IDisposable
{
    private ImmutableArray<ScreenshotAnnotation> _annotations = [];
    private readonly List<ImmutableArray<ScreenshotAnnotation>> _undo = [], _redo = [];
    /// <summary>Gets the original image owned by this editing session.</summary>
    public ScreenshotFrame Frame { get; } = frame;
    /// <summary>Gets immutable current annotations.</summary>
    public ImmutableArray<ScreenshotAnnotation> Annotations => _annotations;
    /// <summary>Gets whether an undo state exists.</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>Gets whether a redo state exists.</summary>
    public bool CanRedo => _redo.Count > 0;
    /// <summary>Validates and adds a complete gesture as one undoable action.</summary>
    public void Add(ScreenshotAnnotation annotation) { Validate(annotation); if (_annotations.Length >= 1000) { throw new InvalidOperationException("Annotation limit reached."); } Commit(_annotations.Add(annotation)); }
    /// <summary>Commits one move/resize/style change, preserving other annotations.</summary>
    public void Update(ScreenshotAnnotation annotation)
    {
        Validate(annotation); int index = Index(annotation.Id);
        if (index < 0) { throw new ArgumentException("Annotation not found.", nameof(annotation)); } Commit(_annotations.SetItem(index, annotation));
    }
    /// <summary>Removes one annotation without changing original pixels.</summary>
    public bool Remove(Guid id) { int index = Index(id); if (index < 0) { return false; } Commit(_annotations.RemoveAt(index)); return true; }
    /// <summary>Finds the topmost annotation under an image-relative pointer.</summary>
    public ScreenshotAnnotation? Hit(ScreenshotPoint point) => _annotations.Reverse().FirstOrDefault(item =>
    { var box = item.Box(); double margin = Math.Max(6, item.Thickness); return point.X >= box.X - margin && point.X <= box.X + box.Width + margin && point.Y >= box.Y - margin && point.Y <= box.Y + box.Height + margin; });
    /// <summary>Restores one complete prior document state.</summary>
    public bool Undo() { if (!CanUndo) { return false; } _redo.Add(_annotations); _annotations = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); return true; }
    /// <summary>Reapplies one undone document state.</summary>
    public bool Redo() { if (!CanRedo) { return false; } _undo.Add(_annotations); _annotations = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); return true; }
    private void Commit(ImmutableArray<ScreenshotAnnotation> next) { _undo.Add(_annotations); if (_undo.Count > 100) { _undo.RemoveAt(0); } _annotations = next; _redo.Clear(); }
    private int Index(Guid id) { for (int index = 0; index < _annotations.Length; index++) { if (_annotations[index].Id == id) { return index; } } return -1; }
    private static void Validate(ScreenshotAnnotation annotation)
    {
        if (!Enum.IsDefined(annotation.Tool) || annotation.Tool is ScreenshotTool.Select or ScreenshotTool.Eraser || annotation.Points.Length is < 1 or > 4096
            || annotation.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000)
            || !double.IsFinite(annotation.Thickness) || annotation.Thickness is < 1 or > 40 || annotation.Text.Length > 2000 || !double.IsFinite(annotation.FontSize) || annotation.FontSize is < 8 or > 240)
        { throw new ArgumentException("Invalid annotation.", nameof(annotation)); }
    }
    /// <inheritdoc />
    public void Dispose() { _annotations = []; _undo.Clear(); _redo.Clear(); Frame.Dispose(); }
}

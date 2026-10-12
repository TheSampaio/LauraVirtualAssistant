using Laura.App.Theming;
using Laura.Core.Brain;
using Microsoft.Maui.Graphics;

namespace Laura.App.Controls;

/// <summary>Native force-directed graph with bounded rendering, stable positions, node dragging, pan, zoom and selection.</summary>
public sealed class BrainGraphView : GraphicsView, IDrawable
{
    private readonly Dictionary<string, PointF> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PointF> _layoutCache = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, int> _communities = new Dictionary<string, int>();
    private string _layoutStamp = "";
    private bool _groupCommunities;
    private BrainNote[] _notes = [];
    private BrainEdge[] _edges = [];
    private PointF _pan, _start, _last;
    private string? _drag;
    private bool _moved;
    private float _zoom = 1;
    private string? _selected;
    private long _lastClick;
    private int _generation;
    /// <summary>Gets the actual rendered node count after filters and integrity checks.</summary>
    public int VisibleNodeCount => _notes.Length;
    /// <summary>Reports node selection without coupling graph geometry to note storage.</summary>
    public event EventHandler<string>? NodeSelected;
    /// <summary>Reports a second click requesting the actual note reader/editor.</summary>
    public event EventHandler<string>? NodeOpened;
    /// <summary>Creates a native interactive canvas with no embedded web framework.</summary>
    public BrainGraphView()
    {
        Drawable = this; BackgroundColor = Theme.Background; MinimumHeightRequest = 120;
        SizeChanged += (_, _) => ResetView();
        StartInteraction += (_, args) =>
        {
            if (args.Touches.Length == 0) { return; } _start = _last = args.Touches[0]; _drag = Hit(_start); _moved = false;
            if (Width >= 450 && Height >= 280 && MapRect().Contains(_start)) { CenterFromMap(_start); _drag = null; _moved = true; return; }
            if (_drag is not null) { _selected = _drag; NodeSelected?.Invoke(this, _drag); Invalidate(); }
        };
        DragInteraction += (_, args) =>
        {
            if (args.Touches.Length == 0) { return; } PointF point = args.Touches[0];
            float dx = point.X - _last.X, dy = point.Y - _last.Y; _last = point;
            _moved |= Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) > 4;
            if (_drag is { } id) { PointF old = _positions[id]; _positions[id] = new(old.X + dx / _zoom, old.Y + dy / _zoom); _layoutCache[id] = _positions[id]; }
            else { _pan = new(_pan.X + dx, _pan.Y + dy); } Invalidate();
        };
        EndInteraction += (_, _) =>
        {
            if (_drag is { } id && !_moved)
            { long now = Environment.TickCount64; if (now - _lastClick < 450) { NodeOpened?.Invoke(this, id); } _lastClick = now; }
            _drag = null;
        };
        HandlerChanged += (_, _) =>
        {
            if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement native)
            {
                native.PointerWheelChanged += (_, args) =>
                {
                    var point = args.GetCurrentPoint(native); Zoom(point.Properties.MouseWheelDelta > 0 ? 1.12f : 1 / 1.12f);
                    args.Handled = true;
                };
                native.PointerMoved += (_, args) =>
                {
                    var point = args.GetCurrentPoint(native).Position; string? id = Hit(new((float)point.X, (float)point.Y));
                    ToolTipProperties.SetText(this, id is null ? "" : _notes.First(note => note.Id == id).Title);
                };
            }
        };
    }
    /// <summary>Highlights file-tree selection without simulating a pointer event.</summary>
    public void Select(string id) { _selected = id; SemanticProperties.SetDescription(this, _notes.FirstOrDefault(note => note.Id == id)?.Title ?? "LAURA Brain graph"); Invalidate(); }
    /// <summary>Toggles derived connected-community layout and colors.</summary>
    public void SetCommunities(bool enabled) { _groupCommunities = enabled; _layoutStamp = ""; }
    /// <summary>Scales the canvas around its center while leaving node coordinates unchanged.</summary>
    public void Zoom(float factor) { _zoom = Math.Clamp(_zoom * factor, .15f, 5); Invalidate(); }
    /// <summary>Returns the current graph to a centered overview.</summary>
    public void ResetView()
    {
        if (_positions.Count == 0 || Width <= 0 || Height <= 0) { _zoom = 1; _pan = new(); Invalidate(); return; }
        float minX = _positions.Values.Min(point => point.X), maxX = _positions.Values.Max(point => point.X), minY = _positions.Values.Min(point => point.Y), maxY = _positions.Values.Max(point => point.Y);
        _zoom = (float)Math.Clamp(Math.Min((Width - 220) / Math.Max(180, maxX - minX), (Height - 140) / Math.Max(140, maxY - minY)), .15, 2);
        _pan = new(-(minX + maxX) * .5f * _zoom, -(minY + maxY) * .5f * _zoom); Invalidate();
    }
    /// <summary>Filters before laying out; local view follows actual authored neighbors, and disconnected nodes remain visible.</summary>
    public async Task SetGraphAsync(BrainSnapshot snapshot, string query, string folder, string type, string? localId, int limit, int depth = 1)
    {
        IEnumerable<BrainNote> candidates = snapshot.Notes.Where(note => !note.Path.StartsWith("archive/", StringComparison.OrdinalIgnoreCase) && note.Path != "README.md"
            && !snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id")
            && (query.Length == 0 || note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            && (folder.Length == 0 || note.Path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) && (type.Length == 0 || note.Type == type));
        if (localId is not null)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal) { localId };
            for (int hop = 0; hop < Math.Clamp(depth, 1, 4); hop++)
            { var frontier = ids.ToHashSet(StringComparer.Ordinal); foreach (BrainEdge edge in snapshot.Edges.Where(edge => frontier.Contains(edge.Source) || frontier.Contains(edge.Target))) { ids.Add(edge.Source); ids.Add(edge.Target); } }
            candidates = candidates.Where(note => ids.Contains(note.Id));
        }
        BrainNote[] notes = candidates.OrderByDescending(note => note.Importance).Take(limit).ToArray();
        var visibleIds = notes.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
        BrainEdge[] edges = snapshot.Edges.Where(edge => visibleIds.Contains(edge.Source) && visibleIds.Contains(edge.Target)).Take(limit * 6).ToArray();
        string stamp = FileBrainStorage.Hash(string.Join('|', notes.Select(note => note.Id + note.Hash)) + string.Join('|', edges) + _groupCommunities);
        if (stamp == _layoutStamp) { Invalidate(); return; } int generation = ++_generation;
        IReadOnlyDictionary<string, int> communities = BrainGraphTopology.Communities(notes, edges);
        var positions = new Dictionary<string, PointF>(StringComparer.Ordinal);
        foreach (BrainNote note in notes)
        {
            if (_layoutCache.TryGetValue(note.Id, out PointF old)) { positions[note.Id] = old; }
            else { uint seed = Convert.ToUInt32(FileBrainStorage.Hash(note.Id)[..8], 16); float angle = seed % 6283 / 1000f; positions[note.Id] = new(MathF.Cos(angle) * 180, MathF.Sin(angle) * 180); }
        }
        bool grouped = _groupCommunities; await Task.Run(() => Layout(notes, edges, positions, grouped ? communities : null)).ConfigureAwait(true);
        if (generation != _generation) { return; }
        bool visibilityChanged = !_notes.Select(note => note.Id).ToHashSet(StringComparer.Ordinal).SetEquals(visibleIds);
        _notes = notes; _edges = edges; _communities = communities;
        _layoutStamp = stamp; _positions.Clear(); if (_layoutCache.Count > 2000) { _layoutCache.Clear(); }
        foreach (var pair in positions) { _positions[pair.Key] = pair.Value; _layoutCache[pair.Key] = pair.Value; }
        if (visibilityChanged) { ResetView(); } else { Invalidate(); }
    }
    /// <summary>Relaxes a bounded graph off the UI thread, with separated connected communities and no arbitrary edges.</summary>
    private static void Layout(BrainNote[] notes, BrainEdge[] edges, Dictionary<string, PointF> positions, IReadOnlyDictionary<string, int>? communities)
    {
        var indices = notes.Select((note, index) => (note.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var points = notes.Select(note => positions[note.Id]).ToArray();
        int communityCount = communities?.Values.Distinct().Count() ?? 0;
        for (int step = 0; step < 45; step++)
        {
            var forces = new PointF[points.Length];
            for (int left = 0; left < points.Length; left++)
            {
                for (int right = left + 1; right < points.Length; right++)
                {
                    float dx = points[left].X - points[right].X, dy = points[left].Y - points[right].Y;
                    float distance = MathF.Max(16, dx * dx + dy * dy); float factor = MathF.Min(4, 1300 / distance);
                    forces[left] = new(forces[left].X + dx * factor, forces[left].Y + dy * factor); forces[right] = new(forces[right].X - dx * factor, forces[right].Y - dy * factor);
                }
            }
            foreach (BrainEdge edge in edges)
            {
                int left = indices[edge.Source], right = indices[edge.Target]; float dx = points[right].X - points[left].X, dy = points[right].Y - points[left].Y;
                float factor = .012f * (float)(.5 + edge.Weight); forces[left] = new(forces[left].X + dx * factor, forces[left].Y + dy * factor); forces[right] = new(forces[right].X - dx * factor, forces[right].Y - dy * factor);
            }
            for (int node = 0; node < points.Length; node++)
            {
                float cooling = .25f * (1 - step / 55f), centerX = 0, centerY = 0;
                if (communities is not null)
                { float angle = 2 * MathF.PI * communities[notes[node].Id] / Math.Max(1, communityCount); centerX = communityCount > 1 ? MathF.Cos(angle) * 150 : 0; centerY = communityCount > 1 ? MathF.Sin(angle) * 150 : 0; }
                points[node] = new(points[node].X + Math.Clamp(forces[node].X - (points[node].X - centerX) * .025f, -18, 18) * cooling, points[node].Y + Math.Clamp(forces[node].Y - (points[node].Y - centerY) * .025f, -18, 18) * cooling);
            }
        }
        for (int node = 0; node < notes.Length; node++) { positions[notes[node].Id] = points[node]; }
    }
    /// <summary>Maps a graph point to the current native canvas.</summary>
    private PointF Screen(PointF point) => new((float)Width / 2 + _pan.X + point.X * _zoom, (float)Height / 2 + _pan.Y + point.Y * _zoom);
    /// <summary>Hit-tests only rendered nodes using a usable pointer target.</summary>
    private string? Hit(PointF point)
    {
        float closest = 484; string? match = null;
        foreach (BrainNote note in _notes) { PointF screen = Screen(_positions[note.Id]); float dx = point.X - screen.X, dy = point.Y - screen.Y, distance = dx * dx + dy * dy; if (distance <= closest) { closest = distance; match = note.Id; } }
        return match;
    }
    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Theme.Background; canvas.FillRectangle(dirtyRect);
        canvas.FillColor = Theme.Muted.WithAlpha(.09f);
        for (float x = 24; x < dirtyRect.Width; x += 32) { for (float y = 24; y < dirtyRect.Height; y += 32) { canvas.FillCircle(x, y, .7f); } }
        foreach (BrainEdge edge in _edges)
        {
            PointF from = Screen(_positions[edge.Source]), to = Screen(_positions[edge.Target]); bool focused = edge.Source == _selected || edge.Target == _selected;
            canvas.StrokeColor = edge.Type == "contradicts" ? Color.FromArgb("#D99842").WithAlpha(.65f) : focused ? Theme.Accent.WithAlpha(.7f) : Theme.Muted.WithAlpha(.16f + (float)edge.Weight * .2f);
            canvas.StrokeSize = focused ? 1.4f : .6f + (float)edge.Weight;
            float dx = to.X - from.X, dy = to.Y - from.Y, bend = focused ? .14f : -.08f;
            PointF control1 = new(from.X + dx * .33f - dy * bend, from.Y + dy * .33f + dx * bend), control2 = new(from.X + dx * .67f - dy * bend, from.Y + dy * .67f + dx * bend);
            var curve = new PathF(); curve.MoveTo(from); curve.CurveTo(control1, control2, to); canvas.DrawPath(curve);
            if (focused)
            {
                float angle = MathF.Atan2(to.Y - control2.Y, to.X - control2.X); PointF end = new(to.X - MathF.Cos(angle) * 14, to.Y - MathF.Sin(angle) * 14);
                canvas.DrawLine(end.X, end.Y, end.X - MathF.Cos(angle - .45f) * 7, end.Y - MathF.Sin(angle - .45f) * 7);
                canvas.DrawLine(end.X, end.Y, end.X - MathF.Cos(angle + .45f) * 7, end.Y - MathF.Sin(angle + .45f) * 7);
            }
        }
        foreach (BrainNote note in _notes)
        {
            PointF center = Screen(_positions[note.Id]); if (!dirtyRect.Inflate(80, 40).Contains(center)) { continue; }
            bool selected = note.Id == _selected; float radius = (selected ? 17 : 7) + (float)note.Importance * 4;
            if (selected) { for (int halo = 5; halo > 0; halo--) { canvas.FillColor = Theme.Accent.WithAlpha(.025f); canvas.FillCircle(center, radius + halo * 7); } }
            canvas.FillColor = selected ? Theme.Accent : note.MemoryType == "episodic" ? Color.FromArgb("#D4ABA1") : note.MemoryType == "procedural" ? Color.FromArgb("#EC879A") : Color.FromArgb("#F47992");
            if (_groupCommunities && !selected) { canvas.FillColor = (_communities[note.Id] % 3) switch { 0 => Color.FromArgb("#E58297"), 1 => Color.FromArgb("#C2A7B3"), _ => Color.FromArgb("#DAB19F") }; }
            canvas.FillCircle(center, radius); canvas.StrokeColor = Color.FromArgb("#FFB6C5").WithAlpha(.6f); canvas.StrokeSize = 1; canvas.DrawCircle(center, radius);
            if (selected || (dirtyRect.Height >= 240 && (_notes.Length < 90 || _zoom > 1.7)))
            { canvas.FontSize = selected ? 14 : 12; canvas.FontColor = selected ? Theme.Text : Theme.Muted; canvas.DrawString(note.Title.Length > 35 ? note.Title[..35] + "…" : note.Title, center.X - 100, center.Y + radius + 8, 200, 30, HorizontalAlignment.Center, VerticalAlignment.Top); }
        }
        if (Width >= 450 && Height >= 280 && _notes.Length > 0) { DrawMap(canvas); }
    }
    /// <summary>Gets the minimap viewport without creating additional native control trees.</summary>
    private RectF MapRect() => new(16, (float)Height - 102, 144, 86);
    /// <summary>Maps the actual graph bounds into the overview, preserving aspect ratio.</summary>
    private (PointF Center, float Scale) MapGeometry()
    {
        if (_positions.Count == 0) { return (new(), 1); }
        float left = _positions.Values.Min(point => point.X), right = _positions.Values.Max(point => point.X), top = _positions.Values.Min(point => point.Y), bottom = _positions.Values.Max(point => point.Y);
        return (new((left + right) / 2, (top + bottom) / 2), Math.Min(120 / Math.Max(80, right - left), 62 / Math.Max(80, bottom - top)));
    }
    /// <summary>Draws real nodes, connections and the current visible region in the native overview.</summary>
    private void DrawMap(ICanvas canvas)
    {
        RectF bounds = MapRect(); var (center, scale) = MapGeometry(); PointF origin = bounds.Center;
        canvas.FillColor = Theme.Surface; canvas.FillRoundedRectangle(bounds, 10); canvas.StrokeColor = Theme.Line; canvas.StrokeSize = 1; canvas.DrawRoundedRectangle(bounds, 10);
        canvas.SaveState(); canvas.ClipRectangle(bounds); canvas.StrokeColor = Theme.Accent.WithAlpha(.3f);
        foreach (BrainEdge edge in _edges) { PointF a = _positions[edge.Source], b = _positions[edge.Target]; canvas.DrawLine(origin.X + (a.X - center.X) * scale, origin.Y + (a.Y - center.Y) * scale, origin.X + (b.X - center.X) * scale, origin.Y + (b.Y - center.Y) * scale); }
        foreach (BrainNote note in _notes) { PointF point = _positions[note.Id]; canvas.FillColor = note.Id == _selected ? Theme.Accent : Theme.Muted; canvas.FillCircle(origin.X + (point.X - center.X) * scale, origin.Y + (point.Y - center.Y) * scale, note.Id == _selected ? 3.5f : 2); }
        canvas.StrokeColor = Theme.Accent.WithAlpha(.6f);
        canvas.DrawRectangle(origin.X + (-_pan.X / _zoom - center.X) * scale - (float)Width / _zoom * scale / 2, origin.Y + (-_pan.Y / _zoom - center.Y) * scale - (float)Height / _zoom * scale / 2, (float)Width / _zoom * scale, (float)Height / _zoom * scale);
        canvas.RestoreState();
    }
    /// <summary>Repositions the visible graph from a pointer on its actual overview.</summary>
    private void CenterFromMap(PointF point)
    { if (_positions.Count == 0) { return; } var (center, scale) = MapGeometry(); PointF origin = MapRect().Center; _pan = new(-(center.X + (point.X - origin.X) / scale) * _zoom, -(center.Y + (point.Y - origin.Y) / scale) * _zoom); Invalidate(); }
}

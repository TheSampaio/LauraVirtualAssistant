using System.Diagnostics;
using System.Numerics;
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
    private IReadOnlyDictionary<string, int> _degrees = new Dictionary<string, int>();
    private PointF _pan, _start, _last;
    private string? _drag;
    private bool _moved;
    private float _zoom = 1;
    private string? _selected;
    private long _lastClick;
    private int _generation;
    private BrainGraphMotion? _motion;
    private IDispatcherTimer? _animation;
    private Microsoft.UI.Xaml.Window? _nativeWindow;
    private bool _loaded, _animationActive = true, _hostVisible = true;
    private long _lastFrame;
    private string? _lastClickId;
    /// <summary>Gets the current visual selection, independently from the note reader.</summary>
    public string? SelectedNodeId => _selected;
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
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        StartInteraction += (_, args) => { if (args.Touches.Length > 0) { BeginPointer(args.Touches[0]); } };
        DragInteraction += (_, args) => { if (args.Touches.Length > 0) { MovePointer(args.Touches[0]); } };
        EndInteraction += (_, _) => EndPointer();
        CancelInteraction += (_, _) => { _motion?.Release(); _drag = null; };
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
                    ToolTipProperties.SetText(this, id is null ? "" : _notes.First(note => note.Id == id).Title + " / " + _notes.First(note => note.Id == id).OriginAgent);
                };
            }
        };
    }
    /// <summary>Pins a hit node or starts a background pan using current screen geometry.</summary>
    private void BeginPointer(PointF point)
    {
        _start = _last = point; _drag = Hit(point); _moved = false;
        if (Width >= 450 && Height >= 280 && MapRect().Contains(point)) { CenterFromMap(point); _drag = null; _moved = true; return; }
        if (_drag is { } id)
        { PointF position = _positions[id]; _motion?.Hold(id, new(position.X, position.Y)); Select(id); NodeSelected?.Invoke(this, id); }
    }
    /// <summary>Moves a held node in world coordinates while its neighbors continue the live simulation.</summary>
    private void MovePointer(PointF point)
    {
        float dx = point.X - _last.X, dy = point.Y - _last.Y; _last = point;
        _moved |= Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) > 4;
        if (_drag is { } id)
        {
            PointF old = _positions[id]; _positions[id] = new(old.X + dx / _zoom, old.Y + dy / _zoom);
            _motion?.Hold(id, new(_positions[id].X, _positions[id].Y)); _layoutCache[id] = _positions[id];
        }
        else { _pan = new(_pan.X + dx, _pan.Y + dy); }
        Invalidate();
    }
    /// <summary>Releases graph tension and clears visual selection only for a background click without panning.</summary>
    private void EndPointer()
    {
        if (_drag is { } id && !_moved)
        { long now = Environment.TickCount64; if (_lastClickId == id && now - _lastClick < 450) { NodeOpened?.Invoke(this, id); } _lastClick = now; _lastClickId = id; }
        else if (_drag is null && !_moved) { Select(null); _lastClickId = null; }
        _motion?.Release(); _drag = null;
    }
    /// <summary>Highlights file-tree selection without simulating a pointer event.</summary>
    public void Select(string? id) { _selected = id; SemanticProperties.SetDescription(this, _notes.FirstOrDefault(note => note.Id == id) is { } note ? note.Title + " / " + note.OriginAgent : "LAURA Brain graph"); Invalidate(); }
    /// <summary>Pauses animation when the workspace is no longer showing its graph.</summary>
    public void SetAnimationActive(bool active)
    { _animationActive = active; if (!active) { _motion?.Release(); _drag = null; } UpdateAnimation(); }
    /// <summary>Starts drawing only after the native view is attached to a visible window.</summary>
    private void OnLoaded(object? sender, EventArgs args)
    {
        _loaded = true;
        _nativeWindow = Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (_nativeWindow is not null) { _nativeWindow.VisibilityChanged += OnWindowVisibilityChanged; _hostVisible = _nativeWindow.Visible; }
        _animation ??= Dispatcher.CreateTimer(); _animation.Interval = TimeSpan.FromMilliseconds(16);
        _animation.Tick -= Animate; _animation.Tick += Animate; UpdateAnimation();
    }
    /// <summary>Stops the timer and detaches the window notification when the graph leaves the visual tree.</summary>
    private void OnUnloaded(object? sender, EventArgs args)
    {
        _loaded = false; _animation?.Stop(); _motion?.Release(); _drag = null;
        if (_nativeWindow is not null) { _nativeWindow.VisibilityChanged -= OnWindowVisibilityChanged; _nativeWindow = null; }
    }
    /// <summary>Suspends graph physics while Laura is hidden in the tray.</summary>
    private void OnWindowVisibilityChanged(object sender, Microsoft.UI.Xaml.WindowVisibilityChangedEventArgs args)
    { _hostVisible = args.Visible; if (!_hostVisible) { _motion?.Release(); _drag = null; } UpdateAnimation(); }
    /// <summary>Resumes from visible elapsed time without accumulating hidden-frame physics.</summary>
    private void UpdateAnimation()
    {
        if (_animation is null) { return; }
        if (_loaded && _animationActive && _hostVisible && _motion is not null && _notes.Length > 0)
        { if (!_animation.IsRunning) { _lastFrame = Stopwatch.GetTimestamp(); _animation.Start(); } }
        else { _animation.Stop(); }
    }
    /// <summary>Integrates lightweight graph physics and redraws node positions with their connected lines.</summary>
    private void Animate(object? sender, EventArgs args)
    {
        long now = Stopwatch.GetTimestamp(); float seconds = (float)Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds; _lastFrame = now;
        if (_motion is null) { return; } _motion.Advance(seconds);
        foreach (BrainNote note in _notes)
        { Vector2 point = _motion.Position(note.Id); _positions[note.Id] = new(point.X, point.Y); _layoutCache[note.Id] = _positions[note.Id]; }
        Invalidate();
    }
    /// <summary>Toggles derived connected-community layout and colors.</summary>
    public void SetCommunities(bool enabled) { _groupCommunities = enabled; _layoutStamp = ""; }
    /// <summary>Scales the canvas around its center while leaving node coordinates unchanged.</summary>
    public void Zoom(float factor) { _zoom = Math.Clamp(_zoom * factor, .02f, 5); Invalidate(); }
    /// <summary>Returns the current graph to a centered overview.</summary>
    public void ResetView()
    {
        if (_positions.Count == 0 || Width <= 0 || Height <= 0) { _zoom = 1; _pan = new(); Invalidate(); return; }
        float minX = _positions.Values.Min(point => point.X), maxX = _positions.Values.Max(point => point.X), minY = _positions.Values.Min(point => point.Y), maxY = _positions.Values.Max(point => point.Y);
        _zoom = (float)Math.Clamp(Math.Min(Math.Max(40, Width - 100) / Math.Max(180, maxX - minX), Math.Max(40, Height - 100) / Math.Max(140, maxY - minY)), .02, 2);
        _pan = new(-(minX + maxX) * .5f * _zoom, -(minY + maxY) * .5f * _zoom); Invalidate();
    }
    /// <summary>Filters before laying out; local view follows actual authored neighbors, and disconnected nodes remain visible.</summary>
    public async Task SetGraphAsync(BrainSnapshot snapshot, string query, string folder, string type, string? localId, int limit, int depth = 1)
    {
        using var measurement = Laura.Core.Diagnostics.LauraDiagnostics.Measure("ui.graph.refresh");
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
        string stamp = FileBrainStorage.Hash(string.Join('|', notes.Select(note => note.Id)) + string.Join('|', edges) + _groupCommunities);
        if (stamp == _layoutStamp) { _notes = notes; _edges = edges; Invalidate(); return; } int generation = ++_generation;
        IReadOnlyDictionary<string, int> communities = BrainGraphTopology.Communities(notes, edges);
        var initial = _layoutCache.ToDictionary(pair => pair.Key, pair => new Vector2(pair.Value.X, pair.Value.Y), StringComparer.Ordinal);
        bool grouped = _groupCommunities;
        IReadOnlyDictionary<string, Vector2> positions = await Task.Run(() =>
        {
            using var layoutMeasurement = Laura.Core.Diagnostics.LauraDiagnostics.Measure("ui.graph.layout");
            return BrainGraphLayout.Calculate(notes, edges, initial, grouped ? communities : null);
        }).ConfigureAwait(true);
        if (generation != _generation) { return; }
        bool visibilityChanged = !_notes.Select(note => note.Id).ToHashSet(StringComparer.Ordinal).SetEquals(visibleIds);
        _motion?.Release(); _drag = null;
        _motion = new BrainGraphMotion(positions, edges);
        _notes = notes; _edges = edges; _communities = communities;
        _degrees = edges.SelectMany(edge => new[] { edge.Source, edge.Target }).GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        _layoutStamp = stamp; _positions.Clear(); if (_layoutCache.Count > 2000) { _layoutCache.Clear(); }
        foreach (var pair in positions) { _positions[pair.Key] = new(pair.Value.X, pair.Value.Y); _layoutCache[pair.Key] = _positions[pair.Key]; }
        UpdateAnimation();
        if (visibilityChanged) { ResetView(); } else { Invalidate(); }
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
            canvas.DrawLine(from, to);
        }
        foreach (BrainNote note in _notes)
        {
            PointF center = Screen(_positions[note.Id]); if (!dirtyRect.Inflate(80, 40).Contains(center)) { continue; }
            bool selected = note.Id == _selected;
            float degree = _degrees.GetValueOrDefault(note.Id);
            float radius = Math.Clamp((4 + MathF.Log2(1 + degree) * 1.5f) * _zoom, 2, 16);
            if (selected) { radius += 3; }
            if (selected) { for (int halo = 5; halo > 0; halo--) { canvas.FillColor = Theme.Accent.WithAlpha(.025f); canvas.FillCircle(center, radius + halo * 7); } }
            BrainNodeAppearance appearance = BrainProvenance.Appearance(note.OriginAgent, selected);
            canvas.FillColor = appearance.Fill is { } color ? Color.FromArgb(color) : Theme.Accent;
            canvas.FillCircle(center, radius);
            canvas.StrokeColor = selected ? Theme.Text : Theme.Line; canvas.StrokeSize = appearance.OutlineWidth;
            canvas.DrawCircle(center, radius + appearance.OutlineOffset);
            if (selected || (dirtyRect.Height >= 240 && (_notes.Length < 90 || _zoom > 1.7)))
            { canvas.FontSize = selected ? 14 : 12; canvas.FontColor = selected ? Theme.Text : Theme.Muted; canvas.DrawString(note.Title.Length > 35 ? note.Title[..35] + "…" : note.Title, center.X - 100, center.Y + radius + 8, 200, 30, HorizontalAlignment.Center, VerticalAlignment.Top); }
        }
        if (Width >= 450 && Height >= 280 && _notes.Length > 0) { DrawMap(canvas); }
    }
    /// <summary>Verifies the actual pointer handlers and timer lifecycle on isolated synthetic graph fixtures.</summary>
    internal async Task VerifyInteractionsForSmokeAsync(string hubId, string neighborId)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Graph interaction diagnostics require isolated UI smoke mode."); }
        if (_animation?.IsRunning != true) { throw new InvalidOperationException("Visible graph animation is stopped."); }
        PointF before = _positions[neighborId], hub = Screen(_positions[hubId]);
        BeginPointer(hub);
        if (_selected != hubId) { throw new InvalidOperationException("Pointer did not select the fixture hub."); }
        MovePointer(new(hub.X + 70, hub.Y + 40));
        await Task.Delay(650).ConfigureAwait(true);
        EndPointer();
        if (_positions[neighborId].Distance(before) < 2) { throw new InvalidOperationException("Connected neighbor did not follow the dragged hub."); }
        BeginPointer(new(4, 4)); EndPointer();
        if (_selected is not null) { throw new InvalidOperationException("Background click did not clear graph selection."); }
        SetAnimationActive(false);
        if (_animation.IsRunning) { throw new InvalidOperationException("Hidden graph animation kept running."); }
        PointF stopped = _positions[neighborId]; await Task.Delay(80).ConfigureAwait(true);
        if (_positions[neighborId] != stopped) { throw new InvalidOperationException("Paused graph continued moving."); }
        SetAnimationActive(true);
        if (!_animation.IsRunning) { throw new InvalidOperationException("Visible graph animation did not resume."); }
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
        foreach (BrainNote note in _notes) { PointF point = _positions[note.Id]; canvas.FillColor = BrainProvenance.Color(note.OriginAgent) is { } color ? Color.FromArgb(color) : Theme.Accent; canvas.FillCircle(origin.X + (point.X - center.X) * scale, origin.Y + (point.Y - center.Y) * scale, note.Id == _selected ? 3.5f : 2); }
        canvas.StrokeColor = Theme.Accent.WithAlpha(.6f);
        canvas.DrawRectangle(origin.X + (-_pan.X / _zoom - center.X) * scale - (float)Width / _zoom * scale / 2, origin.Y + (-_pan.Y / _zoom - center.Y) * scale - (float)Height / _zoom * scale / 2, (float)Width / _zoom * scale, (float)Height / _zoom * scale);
        canvas.RestoreState();
    }
    /// <summary>Repositions the visible graph from a pointer on its actual overview.</summary>
    private void CenterFromMap(PointF point)
    { if (_positions.Count == 0) { return; } var (center, scale) = MapGeometry(); PointF origin = MapRect().Center; _pan = new(-(center.X + (point.X - origin.X) / scale) * _zoom, -(center.Y + (point.Y - origin.Y) / scale) * _zoom); Invalidate(); }
}

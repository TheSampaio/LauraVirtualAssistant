using System.Numerics;

namespace Laura.Core.Brain;

/// <summary>Maintains damped graph motion and pinned-node interaction without platform or rendering dependencies.</summary>
public sealed class BrainGraphMotion
{
    private const float StepSeconds = 1f / 120;
    private const float Separation = 28;
    private readonly Dictionary<string, int> _indices;
    private readonly Vector2[] _positions, _anchors, _velocities, _forces;
    private readonly float[] _masses;
    private readonly (int Left, int Right, float Length)[] _links;
    private readonly Dictionary<(int X, int Y), int> _cells = [];
    private readonly int[] _next;
    private int _held = -1;
    private float _elapsed, _pending;

    /// <summary>
    /// Creates a live simulation at the solved layout, retaining actual link lengths as the relaxed state.
    /// Args: positions supplies finite initial geometry; edges supplies visible authored connections.
    /// </summary>
    public BrainGraphMotion(IReadOnlyDictionary<string, Vector2> positions, IReadOnlyList<BrainEdge> edges)
    {
        _indices = positions.Keys.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        _positions = positions.Values.ToArray(); _anchors = (Vector2[])_positions.Clone();
        _velocities = new Vector2[_positions.Length]; _forces = new Vector2[_positions.Length]; _next = new int[_positions.Length];
        _masses = Enumerable.Repeat(1f, _positions.Length).ToArray();
        _links = edges.Where(edge => edge.Source != edge.Target && _indices.ContainsKey(edge.Source) && _indices.ContainsKey(edge.Target))
            .Select(edge => (Left: _indices[edge.Source], Right: _indices[edge.Target]))
            .Distinct().Select(link => (link.Left, link.Right, MathF.Max(Separation, Vector2.Distance(_positions[link.Left], _positions[link.Right])))).ToArray();
        foreach (var link in _links) { _masses[link.Left]++; _masses[link.Right]++; }
        for (int node = 0; node < _masses.Length; node++) { _masses[node] = MathF.Sqrt(_masses[node]); }
    }

    /// <summary>Returns the current world position for an existing visible note.</summary>
    public Vector2 Position(string id) => _positions[_indices[id]];

    /// <summary>Pins a dragged node to its pointer position while leaving connected nodes free to respond.</summary>
    public void Hold(string id, Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) { return; }
        _held = _indices[id]; _positions[_held] = _anchors[_held] = position; _velocities[_held] = Vector2.Zero;
    }

    /// <summary>Releases a dragged node smoothly and retains the graph's new location as its drift center.</summary>
    public void Release()
    {
        if (_held < 0) { return; }
        _held = -1; Array.Copy(_positions, _anchors, _positions.Length);
    }

    /// <summary>
    /// Advances motion with fixed integration steps so refresh rate does not change the physics.
    /// Args: seconds is elapsed visible time; long gaps are bounded to prevent catch-up stalls.
    /// </summary>
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) { return; }
        _pending += MathF.Min(seconds, .1f);
        while (_pending + .000001f >= StepSeconds) { Step(); _pending -= StepSeconds; }
    }

    /// <summary>Combines gentle drift, real link tension and local collision forces with damped inertia.</summary>
    private void Step()
    {
        _elapsed += StepSeconds;
        for (int node = 0; node < _positions.Length; node++)
        {
            float phase = node * 2.3999632f;
            Vector2 drift = new(MathF.Sin(_elapsed * .7f + phase) * 6, MathF.Cos(_elapsed * .57f + phase) * 6);
            _forces[node] = (_anchors[node] + drift - _positions[node]) * .7f;
        }
        foreach (var link in _links)
        {
            Vector2 delta = _positions[link.Right] - _positions[link.Left];
            float distance = MathF.Max(.01f, delta.Length());
            Vector2 force = delta * ((distance - link.Length) / distance * 9);
            _forces[link.Left] += force; _forces[link.Right] -= force;
        }
        SeparateNeighbors();
        float damping = MathF.Exp(-3 * StepSeconds);
        for (int node = 0; node < _positions.Length; node++)
        {
            if (node == _held) { continue; }
            Vector2 velocity = (_velocities[node] + _forces[node] / _masses[node] * StepSeconds) * damping;
            float speed = velocity.Length();
            _velocities[node] = velocity * (MathF.Min(speed, 500) / MathF.Max(.001f, speed));
            _positions[node] += _velocities[node] * StepSeconds;
        }
    }

    /// <summary>Uses neighboring spatial cells to preserve spacing without all-pairs work on every animation frame.</summary>
    private void SeparateNeighbors()
    {
        _cells.Clear();
        for (int node = 0; node < _positions.Length; node++)
        {
            int x = (int)MathF.Floor(_positions[node].X / Separation), y = (int)MathF.Floor(_positions[node].Y / Separation);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int other = _cells.GetValueOrDefault((x + dx, y + dy), -1);
                    while (other >= 0)
                    {
                        Vector2 delta = _positions[node] - _positions[other]; float distance = delta.Length();
                        if (distance < Separation)
                        {
                            Vector2 direction = distance > .01f ? delta / distance : new(MathF.Cos(node * 2.3999632f), MathF.Sin(node * 2.3999632f));
                            Vector2 force = direction * ((Separation - distance) * 12);
                            _forces[node] += force; _forces[other] -= force;
                        }
                        other = _next[other];
                    }
                }
            }
            _next[node] = _cells.GetValueOrDefault((x, y), -1); _cells[(x, y)] = node;
        }
    }
}

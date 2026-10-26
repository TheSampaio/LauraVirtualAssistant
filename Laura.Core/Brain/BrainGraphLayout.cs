using System.Numerics;

namespace Laura.Core.Brain;

/// <summary>Computes portable graph geometry from authored edges without changing stored knowledge.</summary>
public static class BrainGraphLayout
{
    /// <summary>
    /// Relaxes nodes in an open plane with link springs, repulsion and gradual cooling.
    /// Args: notes and edges define visible topology; initial preserves prior geometry; communities optionally attracts components.
    /// Returns: deterministic world coordinates keyed by note identity.
    /// </summary>
    public static IReadOnlyDictionary<string, Vector2> Calculate(IReadOnlyList<BrainNote> notes, IReadOnlyList<BrainEdge> edges,
        IReadOnlyDictionary<string, Vector2>? initial = null, IReadOnlyDictionary<string, int>? communities = null)
    {
        var indices = notes.Select((note, index) => (note.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var points = new Vector2[notes.Count];
        var forces = new Vector2[notes.Count];
        var links = edges.Where(edge => edge.Source != edge.Target && indices.ContainsKey(edge.Source) && indices.ContainsKey(edge.Target))
            .Select(edge => (Left: indices[edge.Source], Right: indices[edge.Target], Weight: double.IsFinite(edge.Weight) ? (float)Math.Clamp(edge.Weight, 0, 1) : 0)).ToArray();
        int componentCount = communities?.Values.Distinct().Count() ?? 0;
        float extent = MathF.Max(80, MathF.Sqrt(notes.Count) * 42);
        for (int node = 0; node < points.Length; node++)
        {
            // Fill an area rather than a circumference, including when all notes are disconnected.
            float angle = node * 2.3999632f, radius = 42 * MathF.Sqrt(node + .5f);
            points[node] = initial is not null && initial.TryGetValue(notes[node].Id, out Vector2 old) && float.IsFinite(old.X) && float.IsFinite(old.Y)
                ? old : new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }
        for (int step = 0; step < 300; step++)
        {
            Array.Clear(forces);
            for (int left = 0; left < points.Length; left++)
            {
                for (int right = left + 1; right < points.Length; right++)
                {
                    Vector2 delta = points[left] - points[right];
                    float squared = delta.LengthSquared();
                    if (squared < .01f)
                    {
                        float angle = (left + right * 17) * 2.3999632f;
                        delta = new(MathF.Cos(angle), MathF.Sin(angle));
                        squared = 1;
                    }
                    Vector2 force = delta * (180 / MathF.Max(1, squared));
                    forces[left] += force;
                    forces[right] -= force;
                }
            }
            foreach (var link in links)
            {
                Vector2 delta = points[link.Right] - points[link.Left];
                float distance = MathF.Max(.1f, delta.Length());
                Vector2 force = delta * ((distance - 65) / distance * (.05f + link.Weight * .05f));
                forces[link.Left] += force;
                forces[link.Right] -= force;
            }
            float temperature = 20 * MathF.Pow(1 - step / 300f, 1.5f) + .15f;
            for (int node = 0; node < points.Length; node++)
            {
                Vector2 center = Vector2.Zero;
                if (communities is not null && componentCount > 1)
                {
                    float angle = communities[notes[node].Id] * 2.3999632f;
                    float radius = extent * MathF.Sqrt((communities[notes[node].Id] + .5f) / componentCount);
                    center = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                }
                Vector2 force = forces[node] - (points[node] - center) * (communities is null ? .1f : .12f);
                float length = force.Length();
                // Limit vector length, not individual axes, so cooling cannot imprint a rectangular boundary.
                points[node] += force * (MathF.Min(length, temperature) / MathF.Max(.001f, length));
            }
        }
        return notes.Select((note, index) => (note.Id, Position: points[index])).ToDictionary(pair => pair.Id, pair => pair.Position, StringComparer.Ordinal);
    }
}

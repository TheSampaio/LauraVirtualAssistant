using System.Diagnostics;

namespace Laura.Core.Brain;

/// <summary>One directed or explicitly reversed traversal of an existing attributed relation.</summary>
public sealed record BrainActivationStep(BrainEdge Edge, bool Reversed);
/// <summary>A bounded retrieval association; it is not a newly asserted relationship or an execution capability.</summary>
public sealed record BrainActivation(string NoteId, double Score, IReadOnlyList<BrainActivationStep> Path);

/// <summary>Prepares bounded neighborhoods and spreads relevance through authored knowledge only, preserving direction and evidence.</summary>
public sealed class BrainGraphActivation
{
    private sealed record Neighbor(string Target, BrainEdge Edge, bool Reversed)
    {
        internal double Strength => Edge.Weight * Edge.Confidence * (Reversed ? .7 : 1);
    }
    private readonly Dictionary<string, List<Neighbor>> _neighbors = new(StringComparer.Ordinal);
    /// <summary>Creates a graph view for one coherent snapshot, excluding inactive/conflicted knowledge and unsupported relation types.</summary>
    public BrainGraphActivation(BrainSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var conflicts = snapshot.Issues.Where(issue => issue.Code == "conflicting_id").Select(issue => issue.Path).ToHashSet(StringComparer.Ordinal);
        var eligible = snapshot.Notes.Where(note => note.IsKnowledge && note.Confidence >= .35 && !conflicts.Contains(note.Path))
            .GroupBy(note => note.Id, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (BrainEdge edge in snapshot.Edges)
        {
            if (!eligible.Contains(edge.Source) || !eligible.Contains(edge.Target) || edge.Source == edge.Target || edge.Type == "contradicts"
                || !BrainMarkdown.RelationTypes.Contains(edge.Type) || !double.IsFinite(edge.Weight) || !double.IsFinite(edge.Confidence)
                || edge.Weight is <= 0 or > 1 || edge.Confidence is < .2 or > 1) { continue; }
            Add(edge.Source, new(edge.Target, edge, false)); Add(edge.Target, new(edge.Source, edge, true));
        }
        foreach (var list in _neighbors.Values)
        {
            list.Sort((left, right) => { int strength = right.Strength.CompareTo(left.Strength); return strength != 0 ? strength : StringComparer.Ordinal.Compare(left.Target, right.Target); });
        }
    }
    private void Add(string id, Neighbor neighbor)
    {
        if (!_neighbors.TryGetValue(id, out List<Neighbor>? items)) { items = []; _neighbors[id] = items; }
        int equivalent = items.FindIndex(item => item.Target == neighbor.Target && item.Edge.Type == neighbor.Edge.Type && item.Reversed == neighbor.Reversed);
        if (equivalent >= 0) { if (neighbor.Strength > items[equivalent].Strength) { items[equivalent] = neighbor; } return; }
        if (items.Count < 32) { items.Add(neighbor); return; }
        int weakest = 0; for (int index = 1; index < items.Count; index++) { if (items[index].Strength < items[weakest].Strength) { weakest = index; } }
        if (neighbor.Strength > items[weakest].Strength) { items[weakest] = neighbor; }
    }
    /// <summary>Expands up to eight grounded seeds with attenuation, candidate/depth/time budgets and cancellation. Similarity never creates an edge.</summary>
    public IReadOnlyDictionary<string, BrainActivation> Expand(IReadOnlyDictionary<string, double> seeds, BrainOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seeds); ArgumentNullException.ThrowIfNull(options); cancellationToken.ThrowIfCancellationRequested();
        options = options.Sanitized(); var result = new Dictionary<string, BrainActivation>(StringComparer.Ordinal);
        if (options.RetrievalGraphDepth == 0 || seeds.Count == 0) { return result; }
        var clock = Stopwatch.StartNew(); var queue = new PriorityQueue<BrainActivation, (double Score, int Order)>(); int order = 0;
        var selected = seeds.Where(pair => _neighbors.ContainsKey(pair.Key) && double.IsFinite(pair.Value) && pair.Value > 0)
            .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(8).ToArray();
        var seedIds = selected.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal); var visited = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var seed in selected) { double strength = Math.Clamp(seed.Value, 0, 1); queue.Enqueue(new(seed.Key, strength, []), (-strength, order++)); }
        int edgesVisited = 0;
        while (queue.TryDequeue(out BrainActivation? current, out _) && result.Count < options.RetrievalGraphCandidates && clock.ElapsedMilliseconds < options.RetrievalGraphMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited.TryGetValue(current.NoteId, out double previous) && previous >= current.Score) { continue; }
            visited[current.NoteId] = current.Score;
            if (!seedIds.Contains(current.NoteId)) { result[current.NoteId] = current; }
            if (current.Path.Count >= options.RetrievalGraphDepth || !_neighbors.TryGetValue(current.NoteId, out List<Neighbor>? neighbors)) { continue; }
            foreach (Neighbor neighbor in neighbors)
            {
                if (++edgesVisited > 4096 || clock.ElapsedMilliseconds >= options.RetrievalGraphMilliseconds) { return result; }
                double score = current.Score * neighbor.Strength * (current.Path.Count == 0 ? 1 : .55);
                if (score < .05 || visited.GetValueOrDefault(neighbor.Target) >= score || seedIds.Contains(neighbor.Target)) { continue; }
                queue.Enqueue(new(neighbor.Target, score, current.Path.Concat([new BrainActivationStep(neighbor.Edge, neighbor.Reversed)]).ToArray()), (-score, order++));
            }
        }
        return result;
    }
}

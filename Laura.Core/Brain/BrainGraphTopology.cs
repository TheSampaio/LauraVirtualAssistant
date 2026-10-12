namespace Laura.Core.Brain;

/// <summary>Derives connected communities only from actual graph edges, without manufacturing semantic relationships.</summary>
public static class BrainGraphTopology
{
    /// <summary>Assigns deterministic component numbers with iterative path compression.</summary>
    public static IReadOnlyDictionary<string, int> Communities(IReadOnlyList<BrainNote> notes, IReadOnlyList<BrainEdge> edges)
    {
        var parents = notes.ToDictionary(note => note.Id, note => note.Id, StringComparer.Ordinal);
        foreach (BrainEdge edge in edges)
        {
            if (!parents.ContainsKey(edge.Source) || !parents.ContainsKey(edge.Target)) { continue; }
            string left = Root(edge.Source, parents), right = Root(edge.Target, parents);
            if (left != right) { if (StringComparer.Ordinal.Compare(left, right) < 0) { parents[right] = left; } else { parents[left] = right; } }
        }
        var roots = notes.Select(note => Root(note.Id, parents)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        return notes.ToDictionary(note => note.Id, note => roots[Root(note.Id, parents)], StringComparer.Ordinal);
    }
    /// <summary>Resolves a representative without recursion on long imported relation chains.</summary>
    private static string Root(string id, Dictionary<string, string> parents)
    { string root = id; while (parents[root] != root) { root = parents[root]; } while (parents[id] != id) { string next = parents[id]; parents[id] = root; id = next; } return root; }
}

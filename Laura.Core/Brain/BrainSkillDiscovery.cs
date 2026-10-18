using System.Text.Json;

namespace Laura.Core.Brain;

public sealed partial class BrainSkills
{
    private readonly object _discoveryGate = new();
    /// <summary>Ranks only bounded discovery metadata with the existing local hybrid index. Full instructions still require exact review and explicit loading.</summary>
    public async Task<string> DiscoverAsync(string query, int budget = 1500, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > 4000) { throw new ArgumentException("Skill query exceeds its bound.", nameof(query)); }
        budget = Math.Clamp(budget, 0, 16000);
        if (budget < 150) { return ""; }
        await brain.RefreshIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        string vault = brain.Location; BrainSnapshot snapshot = brain.Snapshot;
        return await Task.Run(() =>
        {
            lock (_discoveryGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BrainNote[] skills = snapshot.Notes.Where(note => note.Type == "skill" && note.Status != "archived" && !note.Path.StartsWith("Archive/", StringComparison.OrdinalIgnoreCase)
                    && !snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id"))
                    .GroupBy(note => note.Id, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
                BrainCandidates? candidates = null;
                if (discovery is not null)
                {
                    BrainNote[] metadata = skills.Select(note =>
                    {
                        string description = Clip(note.Description, 240);
                        string content = description + "\n" + Clip(note.Procedure?.Purpose ?? "", 400) + "\n"
                            + string.Join("\n", (note.Procedure?.Capabilities ?? []).Take(8).Select(value => Clip(value, 120))) + "\n"
                            + string.Join("\n", (note.Procedure?.UseWhen ?? []).Take(4).Select(value => Clip(value, 120)));
                        return new BrainNote { Id = note.Id, Path = "SkillMetadata/" + note.Id + ".md", Title = Clip(note.Title, 120), Body = content,
                            Tags = note.Tags.Take(8).Select(value => Clip(value, 80)).ToArray(), Hash = FileBrainStorage.Hash(Clip(note.Title, 120) + "\n" + content + "\n" + string.Join('\n', note.Tags.Take(8).Select(value => Clip(value, 80)))), Source = "derived_skill_metadata" };
                    }).ToArray();
                    discovery.Update(vault, metadata, false);
                    cancellationToken.ThrowIfCancellationRequested(); candidates = discovery.Query(query);
                }
                string mode = discovery is null || string.IsNullOrWhiteSpace(query) ? "unranked_catalogue" : discovery.SemanticAvailable ? "metadata_hybrid" : "metadata_lexical";
                var ranked = skills.Select(note => (Note: note, Lexical: candidates?.Lexical.GetValueOrDefault(note.Id) ?? 0, Semantic: candidates?.Semantic.GetValueOrDefault(note.Id) ?? 0))
                    .Select(item => (item.Note, item.Lexical, item.Semantic, Score: settings.Current.Brain.LexicalWeight * item.Lexical + settings.Current.Brain.SemanticWeight * item.Semantic))
                    .Where(item => mode == "unranked_catalogue" || item.Score > 0).OrderByDescending(item => item.Score).ThenByDescending(item => IsEnabled(item.Note)).ThenBy(item => item.Note.Title, StringComparer.Ordinal).ToArray();
                var entries = new List<object>();
                foreach (var item in ranked.Take(80))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BrainSkillAvailability available = Inspect(item.Note);
                    entries.Add(new { id = item.Note.Id, hash = item.Note.Hash, name = Clip(item.Note.Title, 120), description = Clip(item.Note.Description, 240),
                        capabilities = item.Note.Procedure?.Capabilities.Take(8).Select(value => Clip(value, 120)),
                        availability = new { available.Approved, available.Ready, available.State, Missing = available.Missing.Take(6).Select(value => Clip(value, 160)), omitted = Math.Max(0, available.Missing.Count - 6) },
                        score = item.Score, lexical = item.Lexical, semantic = item.Semantic });
                    if (Serialize().Length > budget) { entries.RemoveAt(entries.Count - 1); }
                    if (entries.Count == 12) { break; }
                }
                if (!vault.Equals(brain.Location, StringComparison.Ordinal)) { throw new IOException("Skill vault changed during discovery."); }
                string result = Serialize(); return result.Length <= budget ? result : "";
                string Serialize() => JsonSerializer.Serialize(new { retrieval = mode, semanticAvailable = discovery?.SemanticAvailable == true, skills = entries });
            }
        }, cancellationToken).ConfigureAwait(false);
    }
    private static string Clip(string value, int bound) => value.Length <= bound ? value : value[..bound];
}

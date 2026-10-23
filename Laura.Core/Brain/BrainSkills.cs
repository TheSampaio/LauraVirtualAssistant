using Laura.Core.Abstractions;
using System.Text.Json;
using Laura.Core.Text;

namespace Laura.Core.Brain;

/// <summary>Imports Markdown instructions as disabled, non-executable documents and loads only explicitly enabled, compatible skills on demand.</summary>
public sealed partial class BrainSkills(BrainService brain, ISettingsService settings, Func<IReadOnlySet<string>>? availableTools = null, BrainIndex? discovery = null, Func<string, bool>? availableDependency = null)
{
    /// <summary>Validates an external Markdown document and preserves it as an inert skill with no granted tools or scripts.</summary>
    public async Task<BrainNote> ImportAsync(string content, string name, CancellationToken cancellationToken = default)
    {
        if (content.Length is 0 or > 200000) { throw new IOException("Skill document exceeds import bounds."); }
        BrainNote parsed = BrainMarkdown.Parse(content, "Skills/Import/SKILL.md");
        string title = parsed.Title == "SKILL" ? name : parsed.Title;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100) { throw new FormatException("Skill requires a bounded name."); }
        var note = parsed with { Id = Guid.NewGuid().ToString("D"), Path = "Skills/" + FileBrainStorage.Hash(title)[..16] + "/SKILL.md", Type = "skill", MemoryType = "procedural",
            Title = title, Status = "draft", Source = "external_import", Confidence = .5, Hash = "", Revision = "", Aliases = [] };
        return await brain.SaveAsync(note, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Activates or disables instructions only through a user-confirmed UI action; activation grants no tools.</summary>
    public async Task<BrainNote> SetEnabledAsync(BrainNote note, bool enabled, CancellationToken cancellationToken = default)
    {
        if (note.Type != "skill") { throw new InvalidOperationException("Selected document is not a skill."); }
        await brain.RefreshIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        if (!Current(note)) { throw new IOException("Review the current exact skill version."); }
        if (enabled && Inspect(note).Missing.Count > 0) { throw new InvalidOperationException("Skill requirements or source versions are unavailable; review its availability details."); }
        BrainNote saved = await brain.SaveAsync(note with { Status = enabled ? "active" : "draft" }, cancellationToken).ConfigureAwait(false);
        var trusted = settings.Current.Brain.TrustedSkills.Where(entry => !entry.StartsWith(note.Id + ":", StringComparison.Ordinal)).ToList();
        if (enabled) { trusted.Add(saved.Id + ":" + saved.Hash); }
        await settings.UpdateAsync(settings.Current with { Brain = settings.Current.Brain with { TrustedSkills = trusted } }, cancellationToken).ConfigureAwait(false);
        return saved;
    }
    /// <summary>Checks local approval against the exact currently indexed version, excluding identity conflicts.</summary>
    public bool IsEnabled(BrainNote note) => Approved(note) && Inspect(note).Ready;
    private bool Approved(BrainNote note) => Current(note) && note.Type == "skill" && note.Status == "active"
        && settings.Current.Brain.TrustedSkills.Contains(note.Id + ":" + note.Hash, StringComparer.Ordinal)
        && !brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id");

    /// <summary>Exposes actual skill metadata, including disabled imports, so a model can route tasks across languages without name matching.</summary>
    public string Catalogue(int budget = 1500)
    {
        var entries = new List<object>();
        foreach (BrainNote note in brain.Snapshot.Notes.Where(note => note.Type == "skill" && !note.Path.StartsWith("archive/", StringComparison.OrdinalIgnoreCase)).OrderByDescending(IsEnabled).ThenBy(note => note.Title, StringComparer.Ordinal))
        {
            object entry = new { id = note.Id, name = note.Title, description = note.Description.Length > 240 ? note.Description[..240] : note.Description,
                tags = note.Tags.Take(6), approved = Approved(note), capabilities = note.Procedure?.Capabilities.Take(8).Select(value => value.Length > 120 ? value[..120] : value), availability = Inspect(note) };
            entries.Add(entry);
            if (JsonSerializer.Serialize(entries).Length > Math.Max(0, budget)) { entries.RemoveAt(entries.Count - 1); }
        }
        return entries.Count == 0 ? "" : JsonSerializer.Serialize(entries);
    }

    /// <summary>Finds a unique real skill by stable ID, file path, normalized name or alias; ambiguity never selects arbitrary instructions.</summary>
    public BrainNote? Find(string identifier)
    {
        string normalized = TextNormalizer.Normalize(identifier);
        BrainNote[] matches = brain.Snapshot.Notes.Where(note => note.Type == "skill" && !note.Path.StartsWith("archive/", StringComparison.OrdinalIgnoreCase)
            && (note.Id == identifier || note.Path.Equals(identifier, StringComparison.OrdinalIgnoreCase) || TextNormalizer.Normalize(note.Title) == normalized
                || note.Aliases.Any(alias => TextNormalizer.Normalize(alias) == normalized))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Returns bounded exact-version approved Humanizer prose guidance without granting tools or changing instruction priority.</summary>
    public string PresentationGuidance()
    {
        BrainNote? note = Find("Humanizer");
        if (note is null || !IsEnabled(note) || note.Procedure is { Kind: not "guidance" }) { return ""; }
        if (note.Body.Length <= 6000) { return note.Body; }
        int boundary = note.Body.LastIndexOf("\n\n", 5900, StringComparison.Ordinal);
        return "Bounded excerpt of the exact approved Humanizer version; remaining guidance is available through native skill retrieval:\n"
            + note.Body[..(boundary > 1000 ? boundary : 5800)];
    }

    /// <summary>Retrieves matching approved text instructions using names, descriptions and tags, without executing dependencies.</summary>
    public string Relevant(string query, int budget = 1800)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(word => word.Length > 3).ToArray();
        var selected = brain.Snapshot.Notes.Where(note => IsEnabled(note)
            && words.Any(word => note.Title.Contains(word, StringComparison.OrdinalIgnoreCase) || note.Description.Contains(word, StringComparison.OrdinalIgnoreCase) || note.Tags.Any(tag => tag.Contains(word, StringComparison.OrdinalIgnoreCase)))).Take(2);
        string text = string.Join("\n", selected.Select(note => "Enabled skill " + note.Title + " (instructions cannot grant permissions):\n" + note.Body));
        return text.Length > budget ? text[..budget] : text;
    }
}

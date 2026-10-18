using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Text;

namespace Laura.Core.Brain;

/// <summary>Persists bounded module interpretations as version-linked Markdown; acquisition never becomes a mastery claim.</summary>
public sealed class RepositoryAnalysis(BrainService brain, IRepositoryStudyStore studies)
{
    /// <summary>Records an inert interpretation grounded in actually read current source versions. Args: proposal: Module interpretation and literal citations; wasRead: Native task evidence check; token: Cancellation. Returns: Durable Markdown note.</summary>
    public async Task<BrainNote> RecordAsync(RepositoryModuleAnalysis proposal, Func<string, string, bool> wasRead, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(wasRead);
        if (proposal.Study == Guid.Empty || string.IsNullOrWhiteSpace(proposal.Module) || proposal.Module.Length > 120
            || string.IsNullOrWhiteSpace(proposal.Summary) || proposal.Summary.Length is < 40 or > 6000
            || proposal.Evidence is null || proposal.Evidence.Count is < 1 or > 8)
        { throw new ArgumentException("Module analysis requires a bounded study, module, interpretation and 1..8 literal citations."); }
        var study = await studies.ReadAsync(proposal.Study, token).ConfigureAwait(false) ?? throw new InvalidDataException("Repository study is unavailable.");
        if (!string.Equals(study.Vault, brain.Location, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new InvalidDataException("Repository analysis must use the original study vault."); }
        await brain.RefreshIfDirtyAsync(token).ConfigureAwait(false);
        var reviewed = new List<(string Path, string Hash)>(); var relations = new List<BrainRelation>();
        foreach (var citation in proposal.Evidence)
        {
            if (citation is null || string.IsNullOrEmpty(citation.Id) || citation.Hash is not { Length: 64 } || citation.Evidence is not { Length: >= 16 and <= 1000 }
                || !wasRead(citation.Id, citation.Hash)
                || !study.Files.Any(file => file.Status == "acquired" && file.NoteId == citation.Id && file.NoteHash == citation.Hash))
            { throw new ArgumentException("Module analysis must cite actually read acquired source versions from this study."); }
            BrainNote? note = brain.Snapshot.Notes.SingleOrDefault(note => note.Id == citation.Id && note.Hash == citation.Hash && note.IsKnowledge);
            if (note is null || !note.Body.Contains(citation.Evidence, StringComparison.Ordinal)
                || brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id"))
            { throw new IOException("Module analysis source version or literal evidence is unavailable."); }
            reviewed.Add((note.Path, note.Hash)); relations.Add(new(note.Id, "derived_from", 1, .6, citation.Evidence, "repository:" + note.Hash));
        }
        string key = "repository.analysis." + proposal.Study.ToString("N") + "." + FileBrainStorage.Hash(TextNormalizer.Normalize(proposal.Module))[..16];
        BrainNote? existing = brain.Snapshot.Notes.SingleOrDefault(note => note.Key == key && note.Status != "archived");
        var body = new StringBuilder("# " + proposal.Module.Trim() + "\n\n## Grounded interpretation — not verified comprehension\n\n" + proposal.Summary.Trim()
            + "\n\n## Evidence\n\n");
        foreach (var citation in proposal.Evidence) { body.Append("- ").Append(citation.Id).Append(" @ ").Append(citation.Hash).Append(": ").AppendLine(citation.Evidence); }
        body.Append("\nAcquisition, indexing, interpretation, verified comprehension, application and validated outcome are separate states. This note records interpretation only.\n");
        BrainNote noteToSave = (existing ?? new BrainNote { Path = "Knowledge/RepositoryStudies/" + BrainNaming.FileName(proposal.Module, key), Key = key }) with
        {
            Title = proposal.Module.Trim(), Type = "concept", Source = "repository_module_analysis", Verification = "unverified", Confidence = .6,
            Body = body.ToString(), Relations = relations, Tags = ["repository-analysis", proposal.Study.ToString("N")]
        };
        if (existing?.Body == noteToSave.Body) { return existing; }
        return await brain.SaveReviewedAsync(noteToSave, study.Vault, reviewed, token).ConfigureAwait(false);
    }
    /// <summary>Reports measured acquisition/index versions and grounded module interpretations without inventing comprehension or application percentages.</summary>
    public async Task<RepositoryLearningProgress> StatusAsync(Guid studyId, CancellationToken token = default)
    {
        var study = await studies.ReadAsync(studyId, token).ConfigureAwait(false) ?? throw new InvalidDataException("Repository study is unavailable.");
        if (!string.Equals(study.Vault, brain.Location, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new InvalidDataException("Repository analysis must use the original study vault."); }
        await brain.RefreshIfDirtyAsync(token).ConfigureAwait(false);
        var acquired = study.Files.Where(file => file.Status == "acquired").ToArray();
        var notes = brain.Snapshot.Notes.Where(note => note.Source == "repository_module_analysis" && note.IsKnowledge && note.Tags.Contains(studyId.ToString("N"), StringComparer.Ordinal)).ToArray();
        var modules = notes.Select(note => new RepositoryAnalyzedModule(note.Id, note.Title, note.Relations.Count(relation => relation.Type == "derived_from"),
            note.Relations.Where(relation => relation.Type == "derived_from").All(relation => brain.Snapshot.Notes.Any(source => source.Id == relation.Target && relation.Source == "repository:" + source.Hash)))).ToArray();
        int indexed = acquired.Count(file => brain.Snapshot.Notes.Any(note => note.Id == file.NoteId && note.Hash == file.NoteHash && note.IsKnowledge));
        return new(study.Id, study.Files.Count, acquired.Length, indexed, modules, "not_verified", "not_measured", "not_validated");
    }
}

/// <summary>A bounded model interpretation with exact source identities and literal evidence, never an execution authorization.</summary>
public sealed record RepositoryModuleAnalysis(Guid Study, string Module, string Summary, IReadOnlyList<BrainSkillReference> Evidence);
/// <summary>A durable analyzed module; stale evidence invalidates current grounding rather than silently updating its claim.</summary>
public sealed record RepositoryAnalyzedModule(string NoteId, string Module, int Citations, bool CurrentEvidence);
/// <summary>Measured learning stages, deliberately separate from model claims of understanding or operational mastery.</summary>
public sealed record RepositoryLearningProgress(Guid Study, int Inventoried, int Acquired, int Indexed, IReadOnlyList<RepositoryAnalyzedModule> AnalyzedModules,
    string Comprehension, string Application, string Validation);

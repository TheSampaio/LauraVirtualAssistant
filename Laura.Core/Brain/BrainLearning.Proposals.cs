using Laura.Core.Text;

namespace Laura.Core.Brain;

public sealed partial class BrainLearning
{
    /// <summary>Validates a contextual interpretation against actual user input. Arbitrary paths, security policies and executable skills cannot be proposed.</summary>
    public async Task<BrainMemoryResult> LearnProposalAsync(BrainMemoryProposal proposal, string userText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(userText);
        if (!Eligible(userText)) { return new("rejected", Reason: "Learning disabled, sensitive input or unsupported size."); }
        if (proposal.Evidence.Length is < 8 or > 2000 || proposal.Value.Length is < 2 or > 500 || proposal.Aspect.Length is < 2 or > 80
            || !userText.Contains(proposal.Evidence, StringComparison.Ordinal) || !proposal.Evidence.Contains(proposal.Value, StringComparison.Ordinal)
            || Sensitive(proposal.Value + " " + proposal.Aspect)) { return new("rejected", Reason: "Proposal lacks bounded, verbatim user evidence."); }
        if (proposal.Change == "temporary") { return new("temporary", Reason: "Working context is retained separately; no durable note created."); }
        if (proposal.Category == "application")
        { return await LearnApplicationAsync(proposal.Aspect, proposal.Value, proposal.Evidence, proposal.Change, proposal.TargetId, brain.Location, cancellationToken).ConfigureAwait(false); }
        (string Type, string Folder, string Memory)? template = proposal.Category switch
        {
            "preference" => ("preference", "Owner/Preferences", "semantic"),
            "goal" => ("goal", "Owner/Goals", "semantic"),
            "project" => ("project", "Projects", "semantic"),
            "decision" => ("decision", "Memories/Decisions", "episodic"),
            "procedure" => ("procedure", "Procedures", "procedural"),
            _ => null
        };
        if (template is null || proposal.Change is not ("new" or "complement" or "correction" or "contradiction" or "obsolete" or "unverified"))
        { return new("rejected", Reason: "Unknown knowledge category or change. No policy or skill modification is supported."); }
        string aspect = TextNormalizer.Normalize(proposal.Aspect);
        bool knownPreference = proposal.Category == "preference" && aspect is "editor" or "language" or "style";
        if (!knownPreference && !userText.Contains(proposal.Aspect, StringComparison.Ordinal)) { return new("rejected", Reason: "Topic is absent from actual user input."); }
        string key = knownPreference ? "owner." + aspect : "context." + proposal.Category + "." + FileBrainStorage.Hash(aspect)[..16];
        string vault = brain.Location;
        await _consolidation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
            BrainNote? existing = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == key);
            if (proposal.TargetId.Length > 0 && (existing is null || existing.Id != proposal.TargetId || existing.Type != template.Value.Type))
            { return new("rejected", Reason: "Correction target does not match the grounded knowledge scope."); }
            if (proposal.Change is "complement" or "correction" or "obsolete" && proposal.TargetId.Length == 0)
            { return new("rejected", Reason: "A stable existing target is required for this change."); }
            var note = new BrainNote
            {
                Key = key, Title = proposal.Value, Body = proposal.Evidence, Type = template.Value.Type, MemoryType = template.Value.Memory,
                Path = template.Value.Folder + "/" + BrainNaming.FileName(knownPreference ? aspect : proposal.Aspect, key),
                Source = "user_context_interpretation", Verification = proposal.Change == "unverified" ? "unverified" : "user_reported", Confidence = .7, Importance = .7,
                Tags = [proposal.Category, proposal.Aspect]
            };
            if (existing is not null && (TextNormalizer.Normalize(existing.Body) == TextNormalizer.Normalize(note.Body)
                || proposal.Change == "new" && TextNormalizer.Normalize(existing.Title) == TextNormalizer.Normalize(note.Title)))
            { return new("repeated", existing.Id); }
            if (proposal.Change == "obsolete")
            {
                BrainNote draft = note with { Status = "draft", Path = "Inbox/" + Guid.NewGuid().ToString("N") + ".md",
                    Relations = [new(existing!.Id, "replaces", 1, .7, proposal.Evidence, note.Source)] };
                BrainNote? prior = brain.Snapshot.Notes.FirstOrDefault(item => item.Status == "draft" && item.Key == key && TextNormalizer.Normalize(item.Body) == TextNormalizer.Normalize(draft.Body) && item.Relations.Any(relation => relation.Type == "replaces"));
                BrainNote savedDraft = prior ?? await brain.SaveAtLocationAsync(draft, vault, cancellationToken).ConfigureAwait(false);
                return new("obsolete_review", savedDraft.Id, "Existing knowledge retained until review.");
            }
            if (proposal.Change == "complement" && existing is not null)
            {
                if (existing.Body.Contains(note.Body, StringComparison.Ordinal)) { return new("repeated", existing.Id); }
                BrainNote saved = await brain.SaveAtLocationAsync(existing with { Body = existing.Body.TrimEnd() + "\n\n" + note.Body, Source = note.Source,
                    Verification = note.Verification, Confidence = Math.Min(existing.Confidence, note.Confidence) }, vault, cancellationToken).ConfigureAwait(false);
                return new("complement", saved.Id);
            }
            BrainNote result = await ConsolidateCoreAsync(note, proposal.Change == "correction", vault, cancellationToken).ConfigureAwait(false);
            return new(result.Status == "draft" ? "contradiction" : existing is null ? "new" : proposal.Change == "correction" ? "correction" : "repeated", result.Id);
        }
        finally { _consolidation.Release(); }
    }
}

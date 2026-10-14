using Laura.Core.Brain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private BrainMaintenance Maintenance(BrainService brain, BrainSkills? skills = null, TimeSpan? interval = null) => new(brain, skills ?? new(brain, _settings), _settings, NullLogger<BrainMaintenance>.Instance, interval);
    private static BrainMaintenanceReference Reference(BrainNote note) => new(note.Id, note.Hash, note.Path);
    [Fact]
    public async Task MaintenanceBatchesOnlyChangedNotesWithoutRewritingKnowledgeAndBoundsItsReport()
    {
        BrainService brain = Create();
        for (int index = 0; index < 23; index++) { await brain.SaveAsync(Note("Knowledge/Repeated" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".md", "Synthetic repeated content", "Identical synthetic source text, requiring a contextual review before any consolidation.")); }
        var before = brain.Snapshot.Notes.ToDictionary(note => note.Path, note => note.Hash, StringComparer.Ordinal);
        await using var maintenance = Maintenance(brain); BrainMaintenanceReport first = await maintenance.RunBatchAsync(3);
        Assert.Equal(3, first.Processed); Assert.Equal(20, first.Remaining); Assert.Contains(first.Findings, item => item.Code == "duplicate_content");
        BrainMaintenanceReport complete = await maintenance.RunBatchAsync(64); Assert.Equal(20, complete.Processed); Assert.Equal(0, complete.Remaining); Assert.Equal(200, complete.Findings.Count); Assert.True(complete.OmittedFindings > 0);
        Assert.Equal(0, (await maintenance.RunBatchAsync()).Processed);
        Assert.All(brain.Snapshot.Notes, note => Assert.Equal(before[note.Path], note.Hash)); Assert.Empty(_settings.Current.Brain.TrustedSkills); Assert.False(_settings.Current.Brain.MaintenanceEnabled);
        Assert.True(complete.TotalMilliseconds >= complete.Milliseconds);
        Assert.True(maintenance.Describe(500).Length <= 500); Assert.DoesNotContain("Identical synthetic source text", maintenance.Describe(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task MaintenanceCleansOnlyIdenticalRelationsWithHistoryAndRejectsAStaleOrDifferentVaultReview()
    {
        BrainService brain = Create(); BrainNote target = await brain.SaveAsync(Note("Knowledge/Target.md", "Target", "Synthetic relation target retained."));
        var relation = new BrainRelation(target.Id, "supports", .6, .8, "Literal reviewed evidence", "manual");
        BrainNote note = await brain.SaveAsync(Note("Knowledge/Source.md", "Source", "Synthetic relation source with preserved text.") with { Relations = [relation, relation, relation with { Confidence = .7 }] });
        await using var maintenance = Maintenance(brain); await maintenance.RunBatchAsync(); string vault = brain.Location;
        Assert.Contains(maintenance.Report.Findings, item => item.Code == "duplicate_relation"); BrainNote cleaned = await maintenance.DeduplicateRelationsAsync(Reference(note), vault);
        Assert.Equal(2, cleaned.Relations.Count); Assert.Equal(note.Id, cleaned.Id); Assert.Equal(note.Body, cleaned.Body); Assert.Contains(cleaned.Relations, item => item.Confidence == .7);
        Assert.Contains(brain.Snapshot.Notes, item => item.Status == "archived" && item.Body == note.Body && item.Relations.Any(item => item.Target == note.Id));
        await Assert.ThrowsAsync<IOException>(() => maintenance.DeduplicateRelationsAsync(Reference(note), vault));
        await brain.ChangeLocationAsync(Path.Combine(_root, "another-maintenance-vault"), true, false); Assert.Empty(maintenance.Report.Findings); await maintenance.RunBatchAsync();
        await Assert.ThrowsAsync<IOException>(() => maintenance.DeduplicateRelationsAsync(Reference(cleaned), vault));
    }
    [Fact]
    public async Task MaintenanceArchivesOnlyReviewedCompatibleExactDuplicatesAndPreservesRecoverableIdsPathsReferencesAndImages()
    {
        BrainService brain = Create(); const string body = "A synthetic shared document with a retained local image.\n\n![Reference](../Assets/Shared.png)";
        BrainNote canonical = await brain.SaveAsync(Note("Knowledge/Canonical.md", "Canonical source", body)); BrainNote duplicate = await brain.SaveAsync(Note("Knowledge/Duplicate.md", "Reviewed copy", body));
        BrainNote referencing = await brain.SaveAsync(Note("Knowledge/Referrer.md", "Referring source", "Read [[id:" + duplicate.Id + "]] for the authored source."));
        var storage = new FileBrainStorage(brain.Location); storage.WriteBytes("Assets/Shared.png", [1, 2, 3]);
        await using var maintenance = Maintenance(brain); await maintenance.RunBatchAsync();
        BrainNote archived = await maintenance.ArchiveDuplicateAsync(Reference(duplicate), Reference(canonical), brain.Location);
        Assert.Equal(duplicate.Id, archived.Id); Assert.Equal(duplicate.Path, archived.Path); Assert.Equal(duplicate.Body, archived.Body); Assert.Equal("archived", archived.Status); Assert.False(archived.IsKnowledge);
        Assert.Equal(canonical.Hash, brain.Snapshot.Notes.Single(note => note.Id == canonical.Id).Hash);
        Assert.Equal(duplicate.Id, BrainService.ResolveLink("id:" + duplicate.Id, referencing.Path, brain.Snapshot.Notes)); Assert.Equal(referencing.Hash, brain.Snapshot.Notes.Single(note => note.Id == referencing.Id).Hash);
        Assert.Equal(new byte[] { 1, 2, 3 }, await brain.ReadAttachmentAsync("Assets/Shared.png"));
        BrainNote revision = brain.Snapshot.Notes.Single(note => note.Path.StartsWith("Archive/Revisions/", StringComparison.Ordinal) && note.Relations.Any(relation => relation.Target == duplicate.Id));
        Assert.Equal("Assets/Shared.png", BrainService.ImageReferences(revision.Body, revision.Path).Single()); Assert.Contains("A synthetic shared document", revision.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(await brain.SearchAsync("synthetic shared document"), hit => hit.Note.Id == duplicate.Id);
        BrainNote restored = await brain.SaveAsync(archived with { Status = "active" }); Assert.True(restored.IsKnowledge); Assert.Equal(duplicate.Id, restored.Id);
        await Assert.ThrowsAsync<IOException>(() => maintenance.ArchiveDuplicateAsync(Reference(duplicate), Reference(canonical), brain.Location));
    }
    [Fact]
    public async Task MaintenanceInvalidatesImageInventoryAndProceduralSourceDependenciesWithoutReprocessingUnchangedNotes()
    {
        BrainService brain = Create(); var storage = new FileBrainStorage(brain.Location); storage.WriteBytes("Assets/Shared.png", [1]);
        BrainNote image = await brain.SaveAsync(Note("Knowledge/Image.md", "Synthetic image reference", "A source containing ![Reference](../Assets/Shared.png)."));
        BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings); BrainNote skill = await skills.ProposeAsync(Procedure(source));
        await using var maintenance = Maintenance(brain, skills); await maintenance.RunBatchAsync(); Assert.DoesNotContain(maintenance.Report.Findings, item => item.Code is "missing_image" or "stale_skill_source");
        File.Delete(Path.Combine(brain.Location, "Assets", "Shared.png")); await brain.SaveAsync(source with { Body = source.Body + "\nCurrent source changed." });
        BrainMaintenanceReport changed = await maintenance.RunBatchAsync(); Assert.Equal(3, changed.Processed); Assert.Equal(0, changed.Remaining);
        Assert.Contains(changed.Findings, item => item.Code == "missing_image" && item.Sources.Any(reference => reference.Id == image.Id));
        Assert.Contains(changed.Findings, item => item.Code == "stale_skill_source" && item.Sources.Any(reference => reference.Id == skill.Id));
        Assert.Equal(0, (await maintenance.RunBatchAsync()).Processed);
    }
    [Fact]
    public async Task MaintenanceReportsOldConflictingUnresolvedOversizedAndSimilarProceduresWithoutInventingObsolescenceOrActivation()
    {
        BrainService brain = Create(); BrainNote old = await brain.SaveAsync(Note("Knowledge/Old.md", "Old synthetic project", "An old project can remain valid; age requires review.") with { Type = "project", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-400) }, true);
        await brain.SaveAsync(Note("Knowledge/Unresolved.md", "Unresolved", "An explicit [[Absent synthetic reference]].") with { Relations = [new(old.Id, "contradicts", Evidence: "Unresolved authored disagreement")] });
        await brain.SaveAsync(Note("Knowledge/Large.md", "Large synthetic source", new string('x', 64001)));
        BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings); BrainSkillProposal proposal = Procedure(source);
        await skills.ProposeAsync(proposal); await skills.ProposeAsync(proposal with { Name = "Another synthetic procedure", Definition = proposal.Definition with { Steps = ["Inspect evidence before any further action."] } });
        await using var maintenance = Maintenance(brain, skills); BrainMaintenanceReport report = await maintenance.RunBatchAsync(64);
        Assert.Contains(report.Findings, item => item.Code == "review_age" && item.Evidence.Contains("does not establish obsolescence", StringComparison.Ordinal));
        Assert.Contains(report.Findings, item => item.Code == "unresolved_conflict"); Assert.Contains(report.Findings, item => item.Code == "ambiguous_or_unresolved_link");
        Assert.Contains(report.Findings, item => item.Code == "oversized_document"); Assert.Contains(report.Findings, item => item.Code == "similar_procedure");
        Assert.True(brain.Snapshot.Notes.Single(note => note.Id == old.Id).IsKnowledge); Assert.Empty(_settings.Current.Brain.TrustedSkills);
    }
    [Fact]
    public async Task MaintenanceSemanticCandidatesRemainSuggestionsAndCannotQualifyAsExactDuplicates()
    {
        BrainService brain = Create(new Embedding(true)); BrainNote first = await brain.SaveAsync(Note("Knowledge/Car.md", "Car transport", "A car carries people through the fictional city."));
        BrainNote second = await brain.SaveAsync(Note("Knowledge/Automobile.md", "Automobile route", "An automobile follows fictional roads to a different destination."));
        await using var maintenance = Maintenance(brain); BrainMaintenanceReport report = await maintenance.RunBatchAsync(); Assert.True(report.SemanticAvailable);
        Assert.Contains(report.Findings, item => item.Code == "semantic_neighbor" && item.Evidence.Contains("not factual equivalence", StringComparison.Ordinal));
        Assert.Empty(brain.Snapshot.Edges); await Assert.ThrowsAsync<IOException>(() => maintenance.ArchiveDuplicateAsync(Reference(first), Reference(second), brain.Location));
        Assert.Equal(first.Hash, brain.Snapshot.Notes.Single(note => note.Id == first.Id).Hash); Assert.Equal(second.Hash, brain.Snapshot.Notes.Single(note => note.Id == second.Id).Hash);
    }
    [Fact]
    public async Task MaintenanceObservesActualDerivedIndexRecoveryWithoutLosingAuthoredKnowledge()
    {
        BrainService brain = Create(); BrainNote source = await brain.SaveAsync(Note("Knowledge/Recover.md", "Synthetic recovery source", "Retain this source after a derived index corruption."));
        await using var maintenance = Maintenance(brain); await maintenance.RunBatchAsync();
        string index = Directory.GetFiles(Path.Combine(_root, "cache"), "brain-index-*.db").Single(); await File.WriteAllTextAsync(index, "synthetic corrupt SQLite bytes");
        BrainMaintenanceReport report = await maintenance.RunBatchAsync(); Assert.Contains(report.Findings, item => item.Code == "derived_index_rebuilt");
        Assert.True(brain.IndexRecoveries > 0); Assert.Equal(source.Hash, brain.Snapshot.Notes.Single(note => note.Id == source.Id).Hash); Assert.Contains(await brain.SearchAsync("derived index corruption"), hit => hit.Note.Id == source.Id);
    }
    [Fact]
    public async Task MaintenanceBackgroundWorkIsOptInAndShutdownPreventsFurtherAnalysis()
    {
        BrainService brain = Create(); await brain.SaveAsync(Note("Knowledge/Background.md", "Synthetic background source", "Read-only maintenance does not authorize mutations."));
        var maintenance = Maintenance(brain, interval: TimeSpan.FromMilliseconds(30)); maintenance.Start(); await Task.Delay(100);
        Assert.Equal(DateTimeOffset.MinValue, maintenance.Report.Timestamp);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); maintenance.Changed += (_, _) => completed.TrySetResult();
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { MaintenanceEnabled = true } }); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(DateTimeOffset.MinValue, maintenance.Report.Timestamp); await maintenance.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => maintenance.RunBatchAsync());
    }
}

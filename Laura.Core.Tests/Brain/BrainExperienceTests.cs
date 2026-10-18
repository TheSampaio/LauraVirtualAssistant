using System.Text.Json;
using Laura.Core.Brain;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private static BrainExecutionObservation Observation(int exitCode, string operation) => new(Guid.NewGuid(), "dotnet.exe", exitCode, FileBrainStorage.Hash(operation), DateTimeOffset.UtcNow);
    [Fact]
    public async Task NativeOutcomesConsolidateAnEpisodeWithoutClaimingGoalSuccessOrPersistingArgumentsAndOutput()
    {
        BrainService brain = Create(); var journal = new SqliteConversationMemory(Path.Combine(_root, "experiences.db"));
        using var experiences = new BrainExperiences(brain, _settings, journal, NullLogger<BrainExperiences>.Instance);
        const string goal = "Resolve a fictional compilation failure."; BrainExecutionObservation failed = Observation(1, "first structured operation");
        Assert.Equal("recorded", (await experiences.ObserveAsync(failed, goal, brain.Location)).Disposition);
        BrainNote original = Assert.Single(brain.Snapshot.Notes, note => note.Type == "experience");
        Assert.Contains("Observed exit code: 1", original.Body, StringComparison.Ordinal);
        BrainExecutionObservation subsequent = Observation(0, "different corrected parameters"); await experiences.ObserveAsync(subsequent, goal, brain.Location);
        BrainNote updated = Assert.Single(brain.Snapshot.Notes, note => note.Type == "experience" && note.IsKnowledge);
        Assert.Equal(original.Id, updated.Id); Assert.Equal("episodic", updated.MemoryType); Assert.Equal("observed_execution", updated.Verification);
        Assert.Contains("Observed exit code: 1", updated.Body, StringComparison.Ordinal); Assert.Contains("Observed exit code: 0", updated.Body, StringComparison.Ordinal);
        Assert.Contains("not independently verified", updated.Body, StringComparison.Ordinal); Assert.DoesNotContain("different corrected parameters", updated.Body, StringComparison.Ordinal);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Key == original.Key);
        string revision = updated.Revision; await experiences.ObserveAsync(subsequent, goal, brain.Location);
        Assert.Equal(revision, brain.Snapshot.Notes.Single(note => note.Id == updated.Id).Revision); Assert.Empty(journal.ReadObservations(FileBrainStorage.Hash(brain.Location)));
    }
    [Fact]
    public async Task ExecutionReceiptsRecoverAsKnowledgeOnlyAfterCrashAndDoNotCrossVaults()
    {
        BrainService brain = Create(); var journal = new SqliteConversationMemory(Path.Combine(_root, "observation-restart.db")); string identity = FileBrainStorage.Hash(brain.Location);
        BrainExecutionObservation actual = Observation(0, "synthetic observed operation");
        journal.RetainObservation(actual.Id, identity, JsonSerializer.Serialize(new BrainObservationReceipt(actual, "Recover an actual fictional observation.")));
        BrainExecutionObservation other = Observation(1, "another vault"); journal.RetainObservation(other.Id, FileBrainStorage.Hash("another-vault"), JsonSerializer.Serialize(new BrainObservationReceipt(other, "Keep another vault isolated.")));
        using var restarted = new BrainExperiences(brain, _settings, new SqliteConversationMemory(Path.Combine(_root, "observation-restart.db")), NullLogger<BrainExperiences>.Instance);
        await restarted.RecoverAsync(); Assert.Empty(journal.ReadObservations(identity)); Assert.Single(journal.ReadObservations(FileBrainStorage.Hash("another-vault")));
        BrainNote saved = Assert.Single(brain.Snapshot.Notes, note => note.Type == "experience");
        journal.RetainObservation(actual.Id, identity, JsonSerializer.Serialize(new BrainObservationReceipt(actual, "Recover an actual fictional observation.")));
        await restarted.RecoverAsync(); Assert.Equal(saved.Revision, brain.Snapshot.Notes.Single(note => note.Id == saved.Id).Revision);
    }
    [Fact]
    public async Task ReflectionIsGroundedVersionPinnedDraftedAndCannotReflectItsOwnHypothesis()
    {
        BrainService brain = Create(); var journal = new SqliteConversationMemory(Path.Combine(_root, "reflection.db"));
        using var experiences = new BrainExperiences(brain, _settings, journal, NullLogger<BrainExperiences>.Instance);
        await experiences.ObserveAsync(Observation(1, "synthetic process"), "Investigate a fictional process failure.", brain.Location);
        BrainNote source = Assert.Single(brain.Snapshot.Notes, note => note.Type == "experience"); const string evidence = "Observed exit code: 1";
        const string lesson = "Inspect the observed nonzero exit before proposing another attempt; this does not identify its cause.";
        Assert.Equal("rejected", (await experiences.ProposeReflectionAsync(source.Id, source.Hash, lesson, "An invented observed basis")).Disposition);
        BrainMemoryResult proposed = await experiences.ProposeReflectionAsync(source.Id, source.Hash, lesson, evidence); Assert.Equal("proposed", proposed.Disposition);
        BrainNote draft = brain.Snapshot.Notes.Single(note => note.Id == proposed.NoteId); Assert.Equal("draft", draft.Status); Assert.Equal("unverified", draft.Verification);
        Assert.Contains(draft.Relations, relation => relation.Target == source.Id && relation.Type == "learned_from");
        Assert.DoesNotContain(await brain.SearchAsync("Inspect the observed nonzero exit"), hit => hit.Note.Id == draft.Id);
        Assert.Equal("repeated", (await experiences.ProposeReflectionAsync(source.Id, source.Hash, lesson, evidence)).Disposition);
        Assert.Equal("rejected", (await experiences.ProposeReflectionAsync(draft.Id, draft.Hash, lesson, evidence)).Disposition);
        await brain.SaveAsync(source with { Body = source.Body + "\nUpdated actual evidence." });
        Assert.Equal("rejected", (await experiences.ProposeReflectionAsync(source.Id, source.Hash, lesson, evidence)).Disposition);
    }
    [Fact]
    public async Task SensitiveOrDisabledExperiencesAreNotStoredAndCorruptReceiptsDoNotBlockHealthyOnes()
    {
        BrainService brain = Create(); var journal = new SqliteConversationMemory(Path.Combine(_root, "observation-privacy.db")); string identity = FileBrainStorage.Hash(brain.Location);
        using var experiences = new BrainExperiences(brain, _settings, journal, NullLogger<BrainExperiences>.Instance);
        Assert.Equal("excluded", (await experiences.ObserveAsync(Observation(0, "secret parameters hashed only"), "Remember my password is fictional.", brain.Location)).Disposition);
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = false } });
        Assert.Equal("excluded", (await experiences.ObserveAsync(Observation(0, "disabled"), "A fictional completed command.", brain.Location)).Disposition);
        Assert.Empty(journal.ReadObservations(identity)); Assert.Empty(brain.Snapshot.Notes);
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = true } });
        journal.RetainObservation(Guid.NewGuid(), identity, "{invalid");
        journal.RetainObservation(Guid.NewGuid(), identity, "{\"Observation\":null,\"UserGoal\":null}");
        BrainExecutionObservation valid = Observation(0, "healthy"); journal.RetainObservation(valid.Id, identity, JsonSerializer.Serialize(new BrainObservationReceipt(valid, "Keep a healthy synthetic observation.")));
        await experiences.RecoverAsync(); Assert.Single(brain.Snapshot.Notes, note => note.Type == "experience"); Assert.Equal(2, journal.ReadObservations(identity).Count);
        journal.ClearHistory(); Assert.Equal(2, journal.ReadObservations(identity).Count); Assert.Empty(journal.ReadAllHistory());
    }
}

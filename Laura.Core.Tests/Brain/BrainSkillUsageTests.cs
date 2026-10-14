using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private async Task<(BrainSkills Skills, BrainNote Note)> ReviewedProcessSkillAsync(BrainService brain)
    {
        BrainNote source = brain.Snapshot.Notes.SingleOrDefault(note => note.Path == "Knowledge/ProcedureSource.md") ?? await ProcedureSourceAsync(brain);
        var skills = new BrainSkills(brain, _settings, () => new HashSet<string> { "run_program" });
        BrainSkillProposal proposal = Procedure(source, "Synthetic native process procedure");
        BrainNote note = await skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Kind = "native", Tools = ["run_program"] } });
        return (skills, await skills.SetEnabledAsync(note, true));
    }
    private BrainSkillUsage Usage(BrainService brain, BrainSkills skills, SqliteConversationMemory journal) => new(brain, skills, _settings, journal, NullLogger<BrainSkillUsage>.Instance);
    [Fact]
    public async Task LoadedInstructionsDoNotCountAsExecutionAndCompletedOutcomesAreVersionedPrivateAndIdempotent()
    {
        BrainService brain = Create(); var (skills, note) = await ReviewedProcessSkillAsync(brain); var journal = new SqliteConversationMemory(Path.Combine(_root, "skill-usage.db"));
        var usage = Usage(brain, skills, journal); var context = new ConversationWorkingContext(Guid.NewGuid(), "Test a synthetic process procedure.");
        Assert.Null(usage.Capture(context.TurnId)); Assert.True(usage.Bind(note, context)); Assert.Empty(usage.Read(note.Id));
        BrainSkillUse use = usage.Capture(context.TurnId)!; BrainExecutionObservation zero = Observation(0, "raw parameters are never persisted");
        Assert.True(await usage.ObserveAsync(use, zero, () => context.TurnId)); Assert.True(await usage.ObserveAsync(use, zero, () => context.TurnId));
        Assert.True(await usage.ObserveAsync(use, Observation(9, "different sensitive-looking payload hashed"), () => context.TurnId));
        var reopened = Usage(brain, skills, new SqliteConversationMemory(Path.Combine(_root, "skill-usage.db")));
        IReadOnlyList<BrainSkillOutcome> evidence = reopened.Read(note.Id, note.Hash); Assert.Equal(2, evidence.Count); Assert.Contains(evidence, item => item.ExitCode == 9);
        Assert.All(evidence, item => { Assert.Equal(note.Hash, item.SkillHash); Assert.Equal("run_program", item.Tool); });
        string serialized = System.Text.Json.JsonSerializer.Serialize(evidence); Assert.DoesNotContain("raw parameters", serialized, StringComparison.Ordinal); Assert.DoesNotContain(context.UserText, serialized, StringComparison.Ordinal);
        Assert.Equal(note.Hash, brain.Snapshot.Notes.Single(item => item.Id == note.Id).Hash); Assert.Equal("unverified", note.Verification);
        BrainNote revised = await brain.SaveAsync(note with { Body = note.Body + "\nA proposed change." });
        Assert.False(skills.IsEnabled(revised)); Assert.Empty(reopened.Read(note.Id, revised.Hash)); Assert.Equal(2, reopened.Read(note.Id).Count);
        journal.ClearHistory(); Assert.Empty(reopened.Read(note.Id)); Assert.False(await usage.ObserveAsync(use, Observation(0, "stale after changed version"), () => context.TurnId));
    }
    [Fact]
    public async Task OtherTurnsVaultsExpiredBindingsRevocationAndDisabledLearningDoNotInheritUsage()
    {
        BrainService brain = Create(); var (skills, note) = await ReviewedProcessSkillAsync(brain); var journal = new SqliteConversationMemory(Path.Combine(_root, "skill-isolation.db"));
        var usage = Usage(brain, skills, journal); var context = new ConversationWorkingContext(Guid.NewGuid(), "Verify an isolated synthetic process."); usage.Bind(note, context);
        BrainSkillUse use = usage.Capture(context.TurnId)!;
        Assert.False(usage.Bind(note, new(Guid.NewGuid(), "My medical diagnosis is private."))); Assert.True(usage.Bind(note, context));
        Assert.Null(usage.Capture(Guid.NewGuid())); Assert.False(await usage.ObserveAsync(use, Observation(0, "another turn"), Guid.NewGuid));
        int turnReads = 0;
        Assert.False(await usage.ObserveAsync(use, Observation(0, "turn changed during refresh"), () => ++turnReads == 1 ? context.TurnId : Guid.NewGuid()));
        Assert.False(await usage.ObserveAsync(use with { LoadedAt = DateTimeOffset.UtcNow.AddMinutes(-11) }, Observation(0, "expired"), () => context.TurnId));
        Assert.False(await usage.ObserveAsync(use with { Vault = "another-vault" }, Observation(0, "wrong vault"), () => context.TurnId));
        Assert.False(await usage.ObserveAsync(use, Observation(0, "before loading") with { Timestamp = use.LoadedAt.AddSeconds(-1) }, () => context.TurnId));
        await _settings.UpdateAsync(_settings.Current with { RememberConversations = false });
        Assert.False(await usage.ObserveAsync(use, Observation(0, "not retained"), () => context.TurnId)); Assert.False(usage.Bind(note, context));
        await _settings.UpdateAsync(_settings.Current with { RememberConversations = true });
        await skills.SetEnabledAsync(note, false); Assert.False(await usage.ObserveAsync(use, Observation(0, "revoked"), () => context.TurnId)); Assert.Empty(usage.Read(note.Id));
    }
    [Fact]
    public async Task GenericGuidanceUnreviewedImportsAndChangedSourcesCannotClaimNativeExecution()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        var usage = Usage(brain, skills, new SqliteConversationMemory(Path.Combine(_root, "skill-nonexecution.db"))); var context = new ConversationWorkingContext(Guid.NewGuid(), "Inspect a fictional procedure.");
        BrainNote draft = await skills.ProposeAsync(Procedure(source)); Assert.False(usage.Bind(draft, context));
        BrainNote approved = await skills.SetEnabledAsync(draft, true); Assert.True(usage.Bind(approved, context));
        Assert.False(await usage.ObserveAsync(usage.Capture(context.TurnId), Observation(0, "undeclared native tool"), () => context.TurnId));
        var (nativeSkills, native) = await ReviewedProcessSkillAsync(brain); var nativeUsage = Usage(brain, nativeSkills, new SqliteConversationMemory(Path.Combine(_root, "skill-source.db")));
        Assert.True(nativeUsage.Bind(native, context)); BrainSkillUse use = nativeUsage.Capture(context.TurnId)!;
        BrainNote actualSource = brain.Snapshot.Notes.Single(item => item.Id == native.Procedure!.Sources[0].Id);
        await brain.SaveAsync(actualSource with { Body = actualSource.Body + "\nChanged source." });
        Assert.False(await nativeUsage.ObserveAsync(use, Observation(0, "changed source"), () => context.TurnId)); Assert.Empty(nativeUsage.Read(native.Id));
    }
    [Fact]
    public async Task WorkingContextIsAtomicTransientAndChangesOnRepeatedUserTextAndHistoryClear()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "turns.db")));
        memory.Initialize(); memory.SetWorkingContext("Same synthetic request."); ConversationWorkingContext first = memory.WorkingContext;
        memory.SetWorkingContext(first.UserText); Assert.NotEqual(first.TurnId, memory.WorkingContext.TurnId); Assert.Equal(first.UserText, memory.WorkingContext.UserText);
        Assert.Empty(memory.ReadRecent(10)); memory.ClearHistory(); Assert.Equal(Guid.Empty, memory.WorkingContext.TurnId); Assert.Empty(memory.WorkingContext.UserText);
    }
    [Fact]
    public async Task ObservedFailureSupportsAReviewDraftRevisionWithoutSelfActivationOrInventedCause()
    {
        BrainService brain = Create(); var (skills, note) = await ReviewedProcessSkillAsync(brain); var journal = new SqliteConversationMemory(Path.Combine(_root, "skill-revision.db"));
        var usage = Usage(brain, skills, journal); var context = new ConversationWorkingContext(Guid.NewGuid(), "Investigate a synthetic procedure failure."); usage.Bind(note, context);
        BrainExecutionObservation failed = Observation(7, "a native failure"); Assert.True(await usage.ObserveAsync(usage.Capture(context.TurnId), failed, () => context.TurnId));
        using var experiences = new BrainExperiences(brain, _settings, journal, NullLogger<BrainExperiences>.Instance); await experiences.ObserveAsync(failed, context.UserText, brain.Location);
        BrainNote episode = brain.Snapshot.Notes.Single(item => item.Type == "experience" && item.IsKnowledge);
        BrainSkillDefinition revised = note.Procedure! with { Steps = ["Inspect the observed failure before attempting another approved execution; its cause remains unverified."], Sources = [new(episode.Id, episode.Hash, "Observed exit code: 7")] };
        BrainNote draft = await skills.ProposeAsync(new(note.Title, note.Description, revised, note.Id, note.Hash));
        Assert.Equal(note.Id, draft.Id); Assert.Equal("draft", draft.Status); Assert.False(skills.IsEnabled(draft)); Assert.Equal("unverified", draft.Verification);
        Assert.Contains(brain.Snapshot.Notes, item => item.Status == "archived" && item.Hash != draft.Hash && item.Body == note.Body);
        Assert.Single(usage.Read(draft.Id, note.Hash)); Assert.Empty(usage.Read(draft.Id, draft.Hash));
    }
}

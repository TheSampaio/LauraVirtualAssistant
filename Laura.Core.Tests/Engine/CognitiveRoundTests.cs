using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Engine;

/// <summary>Tests persistent task cycles, independent budgets, timeouts, recovery and unchanged native effect receipts.</summary>
public sealed class CognitiveRoundTests
{
    private static readonly HashSet<string> Tools = ["probe", "write_file", "plan_task", "task_status", "recall_memory"];
    /// <summary>Explicit recovery may refresh changing status but cannot replay a completed write after a repetition barrier.</summary>
    [Fact]
    public async Task NativeResumeRefreshesStatusWithoutReplayingEffects()
    {
        var journal = new RecordingTaskJournal(); var working = Working(); var run = new CognitiveTaskRun(working.UserText, Tools, taskId: working.TurnId, journal: journal);
        var arguments = JsonSerializer.SerializeToElement(new { path = "fixture" });
        await run.ExecuteAsync("write_file", arguments, _ => Task.FromResult(new AssistantToolResult("Confirmed.")), CancellationToken.None);
        for (int attempt = 0; attempt < 3; attempt++)
        { await run.ExecuteAsync("recall_memory", arguments, _ => Task.FromResult(new AssistantToolResult("Observed.")), CancellationToken.None); }
        await journal.SaveAsync(run.Checkpoint());
        Assert.Equal("repetition", (await journal.ReadAsync(working.TurnId))!.Blocker);
        using var tasks = new CognitiveTasks(journal); await tasks.PrepareResumeAsync(working.TurnId);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var status = await tasks.ExecuteAsync(working, Tools, "task_status", JsonSerializer.SerializeToElement(new { }), (_, _) => Task.FromResult(new AssistantToolResult("Actual status.")), CancellationToken.None);
            Assert.False(status.Failed);
        }
        var replay = await tasks.ExecuteAsync(working, Tools, "write_file", arguments, (_, _) => throw new InvalidOperationException("Completed write replayed."), CancellationToken.None);
        Assert.Equal("blocked", replay.Status);
    }
    /// <summary>Changing provider call IDs cannot repeat a confirmed mutable operation inside the same process.</summary>
    [Fact]
    public async Task ConfirmedMutableEffectCannotRepeatBeforeRestart()
    {
        var run = new CognitiveTaskRun("Perform one synthetic write.", Tools); int writes = 0;
        var arguments = JsonSerializer.SerializeToElement(new { path = "fixture" });
        for (int attempt = 0; attempt < 2; attempt++)
        { await run.ExecuteAsync("write_file", arguments, _ => { writes++; return Task.FromResult(new AssistantToolResult("Confirmed.")); }, CancellationToken.None); }
        Assert.Equal(1, writes); Assert.Single(run.Checkpoint().Operations);
    }
    /// <summary>A failed write-ahead checkpoint never leaves an inference marked active or consumes a cycle.</summary>
    [Fact]
    public async Task FailedAdmissionCanBeRetriedWithoutAnActiveRound()
    {
        var journal = new RecordingTaskJournal { FailFromSave = 1 }; using var tasks = new CognitiveTasks(journal); var working = Working();
        await Assert.ThrowsAsync<IOException>(() => tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        journal.FailFromSave = int.MaxValue;
        Assert.True(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        Assert.Equal(1, tasks.Current(working.TurnId)!.Rounds!.Cycles);
        await tasks.EndCycleAsync(working.TurnId, 1, "gemini", "synthetic", null);
        await tasks.PrepareResumeAsync(working.TurnId);
    }
    /// <summary>A successful inference ends only a cycle; the same durable task can admit another provider cycle.</summary>
    [Fact]
    public async Task CompletedRoundDoesNotCompleteObjectiveOrResetTask()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); var working = Working();
        Assert.True(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        Assert.True(await tasks.AdmitInferenceAsync(working.TurnId, CancellationToken.None));
        await tasks.EndCycleAsync(working.TurnId, 100, "gemini", "synthetic-gemini", null);
        Assert.True(await tasks.SetContinuationAsync(working.TurnId, true, CancellationToken.None));
        Assert.True(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        await tasks.EndCycleAsync(working.TurnId, 150, "chatgpt", "synthetic-chatgpt", null);
        var saved = (await journal.ReadAsync(working.TurnId))!;
        Assert.Equal(2, saved.Rounds!.Cycles); Assert.Equal(250, saved.Rounds.ActiveMilliseconds); Assert.Equal("chatgpt", saved.Rounds.Provider);
        Assert.Equal(working.UserText, saved.Objective); Assert.NotEqual("completed", saved.State);
    }
    /// <summary>One model timeout preserves actual results and permits only bounded justified recovery.</summary>
    [Fact]
    public async Task TimeoutRecoveryKeepsEvidenceAndBudgets()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); var working = Working(); int effects = 0;
        await tasks.BeginCycleAsync(working, Tools, new() { MaximumRecoveries = 1 }, CancellationToken.None);
        var arguments = JsonSerializer.SerializeToElement(new { path = "synthetic" });
        await tasks.ExecuteAsync(working, Tools, "write_file", arguments, (_, _) => { effects++; return Task.FromResult(new AssistantToolResult("Confirmed synthetic write.")); }, CancellationToken.None);
        await tasks.EndCycleAsync(working.TurnId, 40, "gemini", "synthetic", new(ConversationFailureKind.Timeout, true));
        Assert.True(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        await tasks.EndCycleAsync(working.TurnId, 50, "gemini", "synthetic", new(ConversationFailureKind.Timeout));
        Assert.False(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        var saved = (await journal.ReadAsync(working.TurnId))!; Assert.Single(saved.Operations); Assert.Equal(1, effects); Assert.Equal(2, saved.Rounds!.Recoveries);
    }
    /// <summary>Application restart reads preserved cycle state and requires native resumption before a paused task can execute.</summary>
    [Fact]
    public async Task PauseRestartAndExplicitResumeRetainIdentity()
    {
        var journal = new RecordingTaskJournal(); var working = Working();
        using (var tasks = new CognitiveTasks(journal))
        {
            await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None); await tasks.EndCycleAsync(working.TurnId, 25, "gemini", "synthetic", null);
            await tasks.PauseAsync(working.TurnId); Assert.False(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        }
        using var restarted = new CognitiveTasks(journal);
        Assert.Null(restarted.Current(working.TurnId)); Assert.Equal(working.TurnId, Assert.Single(await restarted.ListAsync()).Id);
        Assert.False(await restarted.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        var resumed = await restarted.PrepareResumeAsync(working.TurnId); Assert.Equal(1, resumed.Rounds!.Cycles);
        Assert.True(await restarted.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
        await restarted.EndCycleAsync(working.TurnId, 30, "chatgpt", "synthetic", null);
        Assert.Equal(2, restarted.Current(working.TurnId)!.Rounds!.Cycles);
    }
    /// <summary>Cycle, inference-call, active-time and volume admission limits remain independent and cumulative.</summary>
    [Fact]
    public async Task CumulativeBudgetsStopAutomaticWork()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); var working = Working();
        await tasks.BeginCycleAsync(working, Tools, new() { MaximumCycles = 1, MaximumProviderCalls = 1 }, CancellationToken.None);
        Assert.True(await tasks.AdmitInferenceAsync(working.TurnId, CancellationToken.None)); Assert.False(await tasks.AdmitInferenceAsync(working.TurnId, CancellationToken.None));
        await tasks.EndCycleAsync(working.TurnId, 10, "gemini", "synthetic", null); Assert.False(await tasks.SetContinuationAsync(working.TurnId, true, CancellationToken.None));
        Assert.False(await tasks.BeginCycleAsync(working, Tools, new() { MaximumCycles = 128 }, CancellationToken.None));
        var resumed = await tasks.PrepareResumeAsync(working.TurnId); Assert.Equal(1, resumed.Rounds!.Cycles); Assert.Equal(1, resumed.Rounds.ProviderCalls);
        Assert.True(resumed.Rounds.MaximumCycles > 1);
    }
    /// <summary>Pausing one task leaves another task's cancellation and budgets untouched.</summary>
    [Fact]
    public async Task TaskCancellationIsScoped()
    {
        using var tasks = new CognitiveTasks(new RecordingTaskJournal()); var first = Working(); var second = Working();
        await tasks.BeginCycleAsync(first, Tools, new(), CancellationToken.None); await tasks.EndCycleAsync(first.TurnId, 1, "gemini", "synthetic", null);
        await tasks.BeginCycleAsync(second, Tools, new(), CancellationToken.None); await tasks.PauseAsync(first.TurnId, cancel: true);
        Assert.True(tasks.CycleCancellation(first.TurnId).IsCancellationRequested); Assert.False(tasks.CycleCancellation(second.TurnId).IsCancellationRequested);
    }
    /// <summary>Uncertain effect receipts prevent new cycles regardless of remaining inference budgets.</summary>
    [Fact]
    public async Task UncertainEffectsPreventAutomaticRecovery()
    {
        var journal = new RecordingTaskJournal(); var working = Working(); var run = new CognitiveTaskRun(working.UserText, Tools, taskId: working.TurnId);
        var checkpoint = run.Checkpoint() with { Calls = 1, Operations = [new(1, "write_file", new string('A', 64), "started", "", true)] };
        await journal.SaveAsync(checkpoint); using var tasks = new CognitiveTasks(journal);
        Assert.False(await tasks.BeginCycleAsync(working, Tools, new(), CancellationToken.None));
    }
    /// <summary>Creates an attributed synthetic objective; neither generated text nor stored metadata supplies authority.</summary>
    private static ConversationWorkingContext Working() => new(Guid.NewGuid(), "Complete the synthetic three-stage investigation.");
}

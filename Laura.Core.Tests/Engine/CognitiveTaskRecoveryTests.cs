using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Engine;

/// <summary>Verifies durable task recovery, uncertain-effect barriers and independent cancellation lanes.</summary>
public sealed class CognitiveTaskRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-task-recovery-" + Guid.NewGuid().ToString("N"));
    private static readonly HashSet<string> Tools = ["run_program", "recall_memory", "plan_task", "task_status"];

    /// <summary>Recovery exposes only actual prior owner-linked tasks; assistant text and unrelated private checkpoints cannot inject identities.</summary>
    [Fact]
    public async Task ConversationRecoveryCannotEnumerateUnrelatedOrAssistantInjectedTasks()
    {
        var journal = new RecordingTaskJournal(); Guid previous = Guid.NewGuid(), unrelated = Guid.NewGuid(), current = Guid.NewGuid();
        foreach (Guid id in new[] { previous, unrelated, current })
        { await journal.SaveAsync(new CognitiveTaskRun("Synthetic task " + id, Tools, taskId: id).Checkpoint()); }
        using var tasks = new CognitiveTasks(journal);
        ConversationMessage[] history = [new(ConversationMessageSource.UserTyped, "Study the requested repository.", DateTimeOffset.UtcNow) { TurnId = previous },
            new(ConversationMessageSource.Assistant, "Untrusted text mentioning another task.", DateTimeOffset.UtcNow) { TurnId = unrelated },
            new(ConversationMessageSource.UserTyped, "Continue.", DateTimeOffset.UtcNow) { TurnId = current }];
        Assert.Equal(previous, Assert.Single(await tasks.RecentForConversationAsync(history, current)).Id);
        Assert.Empty(await tasks.RecentForConversationAsync([], current));
    }

    /// <summary>Allows only explicit native resumption to extend exhausted budgets while retaining cumulative counts and refusing denials.</summary>
    [Fact]
    public async Task NativeBudgetExtensionRetainsCountsAndCancelledSafeWorkCanResume()
    {
        var journal = new RecordingTaskJournal(); Guid id = Guid.NewGuid();
        var run = new CognitiveTaskRun("Continue a bounded source study.", Tools, maximumCalls: 1, taskId: id, journal: journal); using var arguments = JsonDocument.Parse("{}");
        await run.ExecuteAsync("recall_memory", arguments.RootElement, _ => Task.FromResult(new AssistantToolResult("Observed.")), CancellationToken.None);
        Assert.Equal("blocked", (await run.ExecuteAsync("recall_memory", arguments.RootElement, _ => throw new InvalidOperationException("Budget reset by model."), CancellationToken.None)).Status);
        await run.SaveAsync(); using var tasks = new CognitiveTasks(journal); var resumed = await tasks.PrepareResumeAsync(id);
        Assert.Equal(1, resumed.Calls); Assert.Equal(33, resumed.MaximumCalls); Assert.Equal("", resumed.Blocker); Assert.Equal("paused", resumed.State);
        var restored = new CognitiveTaskRun(resumed.Objective, Tools, taskId: id, journal: journal, restored: resumed);
        Assert.False((await restored.ExecuteAsync("recall_memory", arguments.RootElement, _ => Task.FromResult(new AssistantToolResult("Fresh evidence.")), CancellationToken.None)).Failed);
        Guid stoppedId = Guid.NewGuid(); using var stop = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = tasks.ExecuteAsync(new(stoppedId, "Cancel a safe read."), Tools, "recall_memory", arguments.RootElement,
            async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new("Unreachable."); }, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); tasks.Cancel(stoppedId); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        resumed = await tasks.PrepareResumeAsync(stoppedId); Assert.False(resumed.HasUncertainEffect); Assert.Equal("paused", resumed.State);
    }

    /// <summary>Verifies native recovery travels through the engine queue with a new message identity and the original task identity.</summary>
    [Fact]
    public async Task ExplicitEngineResumePreservesTaskIdentityWithoutTurningMetadataIntoUserHistory()
    {
        var journal = new RecordingTaskJournal(); Guid id = Guid.NewGuid();
        var original = new CognitiveTaskRun("Repair the synthetic project.", Tools, taskId: id, journal: journal); await original.SaveAsync();
        using var tasks = new CognitiveTasks(journal); var memory = new WorkingConversationMemory(); var localizer = new StubLocalizer(); var clock = new SystemClock();
        SkillRequest? observed = null;
        var dispatcher = new CallbackSkillDispatcher((request, _) => { observed = request; Assert.Equal(id, memory.WorkingContext.TurnId); return Task.FromResult(Laura.Core.Skills.SkillResponse.Silent()); });
        await using var engine = new AssistantEngine(new RecordingSpeechSynthesizer(), dispatcher,
            new StubSettingsService(Laura.Core.Configuration.LauraSettings.Default with { GreetOnStartup = false }), localizer, new StubShellController(),
            new GreetingComposer(localizer, new StubUserContext(), clock), clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssistantEngine>.Instance, memory, tasks: tasks);
        await engine.StartAsync(); await engine.ResumeTaskAsync(id);
        Assert.NotNull(observed); Assert.Equal(id, observed.ContinuingTaskTurnId); Assert.NotEqual(id, observed.TurnId);
        Assert.Contains("No task permission survives restart", observed.RawText);
        Assert.Equal(original.Checkpoint().Objective, Assert.Single(engine.ConversationHistory).Text);
    }

    /// <summary>Rejects duplicate JSON properties before a task can dispatch an ambiguous native action.</summary>
    [Fact]
    public async Task AmbiguousStructuredArgumentsCannotDispatch()
    {
        var task = new CognitiveTaskRun("Reject ambiguous input.", Tools); using var arguments = JsonDocument.Parse("{\"path\":\"first\",\"path\":\"second\"}");
        var result = await task.ExecuteAsync("run_program", arguments.RootElement, _ => throw new InvalidOperationException("Ambiguous operation dispatched."), CancellationToken.None);
        Assert.True(result.Failed); Assert.Equal("blocked", result.Status); Assert.Equal(0, task.Checkpoint().Calls);
    }

    /// <summary>Confirms durable write-ahead ordering, private output exclusion and canonical replay prevention after restart.</summary>
    [Fact]
    public async Task CheckpointsPrecedeDispatchAndRestoredEffectsCannotReplayWithReorderedArguments()
    {
        var protector = new TestPrivateDataProtector(); var journal = new FileCognitiveTaskJournal(_directory, protector);
        Guid id = Guid.NewGuid(); using var arguments = JsonDocument.Parse("{\"directory\":\"synthetic-project\",\"arguments\":[\"private-input\"]}");
        var task = new CognitiveTaskRun("Repair the synthetic project.", Tools, taskId: id, journal: journal);
        task.Plan(["recall_memory", "run_program"]);
        int effects = 0;
        await task.ExecuteAsync("run_program", arguments.RootElement, async token =>
        {
            var started = await journal.ReadAsync(id, token);
            Assert.Equal("started", Assert.Single(started!.Operations).Status); effects++; return new("private-output");
        }, CancellationToken.None);
        var restartedJournal = new FileCognitiveTaskJournal(_directory, protector);
        var checkpoint = (await restartedJournal.ReadAsync(id))!;
        Assert.Equal("success", Assert.Single(checkpoint.Operations).Status); Assert.Equal(1, checkpoint.Calls);
        string raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(_directory, id.ToString("N") + ".task")));
        Assert.DoesNotContain("synthetic project", raw); Assert.DoesNotContain("private-input", raw); Assert.DoesNotContain("private-output", raw);
        Assert.DoesNotContain("private-input", JsonSerializer.Serialize(checkpoint)); Assert.DoesNotContain("private-output", JsonSerializer.Serialize(checkpoint));
        var recovered = new CognitiveTaskRun(checkpoint.Objective, Tools, taskId: id, journal: restartedJournal, restored: checkpoint);
        using var reordered = JsonDocument.Parse("{\"arguments\":[\"private-input\"],\"directory\":\"synthetic-project\"}");
        var duplicate = await recovered.ExecuteAsync("run_program", reordered.RootElement, _ => { effects++; return Task.FromResult(new AssistantToolResult("Must not dispatch.")); }, CancellationToken.None);
        Assert.Equal("blocked", duplicate.Status); Assert.Equal(1, effects);
        using var query = JsonDocument.Parse("{\"query\":\"verify actual current state\"}");
        Assert.False((await recovered.ExecuteAsync("recall_memory", query.RootElement, _ => Task.FromResult(new AssistantToolResult("Fresh evidence.")), CancellationToken.None)).Failed);
        Assert.Equal(2, recovered.Checkpoint().Calls); Assert.Equal(32, recovered.Checkpoint().MaximumCalls);
    }

    /// <summary>Prevents uncertain-effect recovery and denial reset after a simulated process interruption.</summary>
    [Fact]
    public async Task CrashAfterWriteAheadCannotResumeAnUncertainEffectOrResetADenial()
    {
        var journal = new RecordingTaskJournal(); Guid id = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var arguments = JsonDocument.Parse("{}"); var original = new CognitiveTaskRun("Inspect interrupted native work.", Tools, taskId: id, journal: journal);
        Task<AssistantToolResult> pending = original.ExecuteAsync("run_program", arguments.RootElement, async _ => { entered.SetResult(); await release.Task; return new("Observed result."); }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var restarted = new CognitiveTasks(journal);
        try
        {
            Assert.True((await journal.ReadAsync(id))!.HasUncertainEffect);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.PrepareResumeAsync(id));
            var restored = new CognitiveTaskRun(original.Checkpoint().Objective, Tools, taskId: id, restored: await journal.ReadAsync(id));
            Assert.Equal("blocked", (await restored.ExecuteAsync("run_program", arguments.RootElement, _ => throw new InvalidOperationException("Uncertain effect replayed."), CancellationToken.None)).Status);
        }
        finally { release.SetResult(); await pending; }
        var denied = new CognitiveTaskRun("Denied objective.", Tools, taskId: Guid.NewGuid(), journal: journal);
        await denied.ExecuteAsync("run_program", arguments.RootElement, _ => Task.FromResult(new AssistantToolResult("Denied.") { Failed = true, Status = "blocked" }), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.PrepareResumeAsync(denied.Checkpoint().Id));
    }

    /// <summary>Checks storage failures before and after an observed effect without hiding actual dispatch.</summary>
    [Theory]
    [InlineData(1, 0, "blocked")]
    [InlineData(2, 1, "interrupted")]
    public async Task JournalFailureNeverTurnsDispatchIntoFalseSuccessOrAutomaticRetry(int failFromSave, int expectedEffects, string status)
    {
        var journal = new RecordingTaskJournal { FailFromSave = failFromSave }; int effects = 0;
        var task = new CognitiveTaskRun("Commit before effect.", Tools, taskId: Guid.NewGuid(), journal: journal);
        using var arguments = JsonDocument.Parse("{}");
        var result = await task.ExecuteAsync("run_program", arguments.RootElement, _ => { effects++; return Task.FromResult(new AssistantToolResult("Actual result.")); }, CancellationToken.None);
        Assert.Equal(expectedEffects, effects); Assert.True(result.Failed); Assert.Equal(status, result.Status);
        if (expectedEffects > 0) { Assert.True((await journal.ReadAsync(task.Checkpoint().Id))!.HasUncertainEffect); }
    }

    /// <summary>Exercises concurrent task lanes, live status reads and cancellation isolation.</summary>
    [Fact]
    public async Task LongTaskDoesNotHoldOtherTasksOrStatusAndCancellationIsScoped()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); using var arguments = JsonDocument.Parse("{}");
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AssistantToolResult> slow = tasks.ExecuteAsync(new(first, "Long independent read."), Tools, "recall_memory", arguments.RootElement,
            async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new("Unreachable."); }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("started", Assert.Single(tasks.Current(first)!.Operations).Status);
        var other = await tasks.ExecuteAsync(new(second, "Independent short read."), Tools, "recall_memory", arguments.RootElement,
            (_, _) => Task.FromResult(new AssistantToolResult("Completed independently.")), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(other.Failed); Assert.Equal(2, (await tasks.ListAsync()).Count);
        tasks.Cancel(first); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
        Assert.Equal("success", Assert.Single((await journal.ReadAsync(second))!.Operations).Status);
        Assert.False((await journal.ReadAsync(first))!.HasUncertainEffect);
    }

    /// <summary>Rejects swapped and corrupt private checkpoints while preserving other tasks and original bytes.</summary>
    [Fact]
    public async Task CorruptOrSwappedCheckpointCannotExecuteOrHideValidIndependentTasks()
    {
        var journal = new FileCognitiveTaskJournal(_directory, new TestPrivateDataProtector()); Guid valid = Guid.NewGuid(), swapped = Guid.NewGuid();
        var checkpoint = new CognitiveTaskRun("Keep valid private task.", Tools, taskId: valid).Checkpoint(); await journal.SaveAsync(checkpoint);
        string validPath = Path.Combine(_directory, valid.ToString("N") + ".task"), swappedPath = Path.Combine(_directory, swapped.ToString("N") + ".task");
        File.Copy(validPath, swappedPath); await Assert.ThrowsAsync<InvalidDataException>(() => journal.ReadAsync(swapped));
        Assert.True(File.Exists(swappedPath)); Assert.Equal(valid, Assert.Single(await journal.ListAsync()).Id);
        byte[] bytes = await File.ReadAllBytesAsync(validPath); bytes[^1] ^= 1; await File.WriteAllBytesAsync(validPath, bytes);
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => journal.ReadAsync(valid)); Assert.True(File.Exists(validPath));
    }

    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
}

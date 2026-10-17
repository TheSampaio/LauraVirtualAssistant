using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Engine;

namespace Laura.Core.Tests.Engine;

/// <summary>Verifies orchestration budgets and recorded adapter outcomes without simulating model selection as a real conversation.</summary>
public sealed class CognitiveTaskRunTests
{
    private static readonly HashSet<string> Tools = ["recall_memory", "study_github_repository", "list_brain_skills", "propose_brain_skill", "plan_task", "task_status", "list_capabilities", "run_program"];
    /// <summary>Reading study coverage cannot create a background acquisition objective or continue unrelated work.</summary>
    [Fact]
    public async Task StatusQuestionCannotStartAutomaticAcquisition()
    {
        var tools = new HashSet<string>(Tools) { "repository_study_status", "continue_repository_study" };
        var task = new CognitiveTaskRun("Report the existing study coverage.", tools);
        var progress = new AssistantToolResult(JsonSerializer.Serialize(new { Id = Guid.NewGuid(), PendingFiles = 3000, Cursor = "current", Documents = Array.Empty<object>(), Failure = "" }));
        await task.ExecuteAsync("repository_study_status", JsonSerializer.SerializeToElement(new { }), _ => Task.FromResult(progress), CancellationToken.None);
        Assert.Null(task.PendingContinuation()); Assert.Null(task.NextSourceOperation()); Assert.Empty(task.Checkpoint().RepositoryStudies!);
    }

    /// <summary>Keeps adapter refusals consistent in returned model results and durable task receipts, even when an adapter omitted its status label.</summary>
    [Fact]
    public async Task AdapterFailureCannotRetainTheDefaultSuccessLabel()
    {
        var task = new CognitiveTaskRun("Inspect an observed adapter refusal.", Tools); using var arguments = JsonDocument.Parse("{}");
        var result = await task.ExecuteAsync("run_program", arguments.RootElement, _ => Task.FromResult(new AssistantToolResult("Synthetic validation refused.") { Failed = true }), CancellationToken.None);
        Assert.True(result.Failed); Assert.Equal("failed", result.Status); Assert.Equal("failed", Assert.Single(task.Checkpoint().Operations).Status);
    }

    /// <summary>Continues real persisted inventory cursors without the old identical-operation loop guard blocking later batches.</summary>
    [Fact]
    public async Task PersistedStudyContinuesDistinctCursorsAndRestoresItsNativeIdentity()
    {
        var tools = new HashSet<string>(Tools) { "start_repository_study", "continue_repository_study", "repository_study_status" };
        Guid id = Guid.NewGuid(); var task = new CognitiveTaskRun("Study the entire synthetic repository incrementally.", tools);
        using var start = JsonDocument.Parse("{\"source\":\"synthetic-project\"}");
        AssistantToolResult Progress(int pending, string cursor) => new(JsonSerializer.Serialize(new { Id = id, PendingFiles = pending, Cursor = cursor, Documents = Array.Empty<object>(), Failure = "" }));
        await task.ExecuteAsync("start_repository_study", start.RootElement, _ => Task.FromResult(Progress(5000, "cursor-0")), CancellationToken.None);
        for (int step = 0; step < 3; step++)
        {
            var next = task.NextSourceOperation(); Assert.NotNull(next); Assert.Equal("continue_repository_study", next.Name);
            Assert.Equal("cursor-" + step, next.Arguments.GetProperty("cursor").GetString());
            Assert.False((await task.ExecuteAsync(next.Name, next.Arguments, _ => Task.FromResult(Progress(4936 - step * 64, "cursor-" + (step + 1))), CancellationToken.None)).Failed);
        }
        Assert.Equal(id, Assert.Single(task.Checkpoint().RepositoryStudies!));
        var restored = new CognitiveTaskRun(task.Checkpoint().Objective, tools, taskId: task.Checkpoint().Id, restored: task.Checkpoint());
        var status = restored.NextSourceOperation(); Assert.NotNull(status); Assert.Equal("repository_study_status", status.Name); Assert.Equal(id, status.Arguments.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MultipleToolsRetainOriginalObjectiveAndObservedIntermediateResults()
    {
        var task = new CognitiveTaskRun("Study the requested repository and evaluate a reusable procedure.", Tools);
        task.Plan(["recall_memory", "study_github_repository", "list_brain_skills", "propose_brain_skill"]);
        using var arguments = JsonDocument.Parse("{}"); int dispatched = 0;
        foreach (string name in new[] { "recall_memory", "study_github_repository", "list_brain_skills", "propose_brain_skill" })
        { await task.ExecuteAsync(name, arguments.RootElement, _ => { dispatched++; return Task.FromResult(new AssistantToolResult("Observed " + name)); }, CancellationToken.None); }
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(task.Snapshot()));
        Assert.Equal(4, dispatched); Assert.Contains("requested repository", state.RootElement.GetProperty("objective").GetString());
        Assert.Equal("operations_completed", state.RootElement.GetProperty("state").GetString());
        Assert.False(state.RootElement.GetProperty("goalVerified").GetBoolean());
        Assert.All(state.RootElement.GetProperty("steps").EnumerateArray(), step => Assert.Equal("completed", step.GetProperty("State").GetString()));
        Assert.Contains("Observed study_github_repository", JsonSerializer.Serialize(task.Snapshot()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DenialBlocksLaterEffectsAndCannotBeResetByModelPlanning()
    {
        var task = new CognitiveTaskRun("Study a source.", Tools); using var args = JsonDocument.Parse("{}"); int effects = 0;
        await task.ExecuteAsync("study_github_repository", args.RootElement, _ => Task.FromResult(new AssistantToolResult("Denied.") { Failed = true, Status = "blocked" }), CancellationToken.None);
        AssistantToolResult alternative = await task.ExecuteAsync("run_program", args.RootElement, _ => { effects++; return Task.FromResult(new AssistantToolResult("Executed.")); }, CancellationToken.None);
        Assert.Equal(0, effects); Assert.Equal("blocked", alternative.Status);
        Assert.Throws<ArgumentException>(() => task.Plan(["recall_memory", "run_program"]));
        var newUserTask = new CognitiveTaskRun("New explicit objective after reviewing the denial.", Tools);
        await newUserTask.ExecuteAsync("run_program", args.RootElement, _ => { effects++; return Task.FromResult(new AssistantToolResult("Its adapter still requests concrete approval.")); }, CancellationToken.None);
        Assert.Equal(1, effects);
    }

    [Fact]
    public async Task LimitsStopLoopsUnknownToolsAndExcessiveCallsBeforeDispatch()
    {
        var task = new CognitiveTaskRun("Bounded task.", Tools, maximumCalls: 3); using var args = JsonDocument.Parse("{}"); int dispatched = 0;
        Task<AssistantToolResult> Execute(string tool) => task.ExecuteAsync(tool, args.RootElement, _ => { dispatched++; return Task.FromResult(new AssistantToolResult("Observed.")); }, CancellationToken.None);
        Assert.Equal("blocked", (await Execute("arbitrary_code")).Status); Assert.Equal(0, dispatched);
        await Execute("recall_memory"); await Execute("recall_memory");
        Assert.Equal("blocked", (await Execute("recall_memory")).Status); Assert.Equal(2, dispatched);
        var capped = new CognitiveTaskRun("One call.", Tools, maximumCalls: 1);
        await capped.ExecuteAsync("recall_memory", args.RootElement, _ => Task.FromResult(new AssistantToolResult("Observed.")), CancellationToken.None);
        Assert.Equal("blocked", (await capped.ExecuteAsync("run_program", args.RootElement, _ => throw new InvalidOperationException("Must not dispatch."), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task CancellationAndDurationLimitsPreserveEarlierObservations()
    {
        var task = new CognitiveTaskRun("Cancel a long operation.", Tools, maximumDuration: TimeSpan.FromMilliseconds(100)); using var args = JsonDocument.Parse("{}");
        await task.ExecuteAsync("recall_memory", args.RootElement, _ => Task.FromResult(new AssistantToolResult("Retained source ID and hash.")), CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.ExecuteAsync("study_github_repository", args.RootElement, async token =>
        { await Task.Delay(TimeSpan.FromSeconds(10), token); return new("Must not complete."); }, CancellationToken.None));
        string snapshot = JsonSerializer.Serialize(task.Snapshot()); Assert.Contains("Retained source ID and hash", snapshot, StringComparison.Ordinal); Assert.Contains("cancelled", snapshot, StringComparison.Ordinal);
        Assert.Equal("blocked", (await task.ExecuteAsync("run_program", args.RootElement, _ => throw new InvalidOperationException("Must not dispatch."), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ContextRetentionIsBoundedAndPlanningCannotInventCapabilitiesOrResetBudgets()
    {
        var task = new CognitiveTaskRun("Keep bounded evidence.", Tools); using var args = JsonDocument.Parse("{}");
        Assert.Throws<ArgumentException>(() => task.Plan(["read_any_secret", "run_program"]));
        task.Plan(["recall_memory", "list_brain_skills"]); Assert.Throws<ArgumentException>(() => task.Plan(["recall_memory", "run_program"]));
        foreach (string tool in new[] { "recall_memory", "list_brain_skills", "propose_brain_skill", "study_github_repository", "run_program" })
        { await task.ExecuteAsync(tool, args.RootElement, _ => Task.FromResult(new AssistantToolResult(new string('x', 10000))), CancellationToken.None); }
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(task.Snapshot()));
        Assert.Equal(5, snapshot.RootElement.GetProperty("calls").GetInt32());
        Assert.True(snapshot.RootElement.GetProperty("observations").EnumerateArray().Sum(observation => observation.GetProperty("Result").GetString()!.Length) <= 16000);
        Assert.All(snapshot.RootElement.GetProperty("observations").EnumerateArray(), observation => Assert.True(observation.GetProperty("Truncated").GetBoolean()));
    }

    [Fact]
    public async Task NativeContinuationChoosesOnlyRegisteredKnowledgeOperationsFromFrozenEvidence()
    {
        var task = new CognitiveTaskRun("Study the source and assess its procedure.", Tools);
        using var args = JsonDocument.Parse("{}"); string hash = new('a', 64);
        await task.ExecuteAsync("study_github_repository", args.RootElement, _ => Task.FromResult(new AssistantToolResult(
            JsonSerializer.Serialize(new { Documents = new[] { new { Id = "source-id", Hash = hash, Path = "README.md" } } }))), CancellationToken.None);
        Assert.Equal("recall_memory", task.NextSourceOperation()!.Name); // This fixture has no registered passage reader: never invent one.
        var registered = Tools.Append("read_brain_passage").ToHashSet(StringComparer.Ordinal); task = new("Study the source and assess its procedure.", registered);
        await task.ExecuteAsync("study_github_repository", args.RootElement, _ => Task.FromResult(new AssistantToolResult(
            JsonSerializer.Serialize(new { Documents = new[] { new { Id = "source-id", Hash = hash, Path = "README.md" } } }))), CancellationToken.None);
        foreach (string expected in new[] { "read_brain_passage", "recall_memory", "list_brain_skills" })
        {
            CognitiveTaskOperation next = Assert.IsType<CognitiveTaskOperation>(task.NextSourceOperation()); Assert.Equal(expected, next.Name);
            await task.ExecuteAsync(next.Name, next.Arguments, _ => Task.FromResult(new AssistantToolResult("Actual native metadata.")), CancellationToken.None, "native_continuation");
        }
        Assert.Null(task.NextSourceOperation()); Assert.NotNull(task.PendingContinuation()); task.AssessSource("source-id", hash); Assert.Null(task.PendingContinuation());
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(task.Snapshot()));
        Assert.Equal(3, snapshot.RootElement.GetProperty("observations").EnumerateArray().Count(observation => observation.GetProperty("Initiator").GetString() == "native_continuation"));
    }
}

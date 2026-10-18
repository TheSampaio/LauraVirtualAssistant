using System.Globalization;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Tests native complexity, evidence-based stalls, distinct recovery causes and provider continuity without cloud calls.</summary>
public sealed class CognitiveTaskRoutingTests
{
    /// <summary>Only heterogeneous, evidenced pending plans beyond the first cycle justify automatic planning escalation.</summary>
    [Theory]
    [InlineData(4, 2, 1, 2, CognitiveTaskDemand.Planning)]
    [InlineData(2, 2, 1, 2, CognitiveTaskDemand.Routine)]
    [InlineData(4, 1, 1, 2, CognitiveTaskDemand.Routine)]
    [InlineData(4, 2, 0, 2, CognitiveTaskDemand.Routine)]
    [InlineData(4, 2, 1, 1, CognitiveTaskDemand.Routine)]
    public void ComplexityNeedsMultipleNativeSignals(int steps, int tools, int observed, int cycles, CognitiveTaskDemand expected)
    {
        var checkpoint = Checkpoint(steps, tools, observed) with { Rounds = new(Cycles: cycles, Provider: "gemini") };
        Assert.Equal(expected, CognitiveTaskRoutingPolicy.Evaluate(checkpoint).Demand);
    }
    /// <summary>Timeout, context, transport and account failures cannot be reclassified as cognitive inability.</summary>
    [Theory]
    [InlineData(ConversationFailureKind.Timeout, CognitiveRecoveryAction.SplitStep)]
    [InlineData(ConversationFailureKind.ContextExceeded, CognitiveRecoveryAction.ReduceContext)]
    [InlineData(ConversationFailureKind.Network, CognitiveRecoveryAction.AwaitProvider)]
    [InlineData(ConversationFailureKind.CapacityLimit, CognitiveRecoveryAction.AwaitProvider)]
    [InlineData(ConversationFailureKind.Unavailable, CognitiveRecoveryAction.AwaitProvider)]
    [InlineData(ConversationFailureKind.UsageLimit, CognitiveRecoveryAction.AwaitOwner)]
    [InlineData(ConversationFailureKind.Authentication, CognitiveRecoveryAction.AwaitOwner)]
    [InlineData(ConversationFailureKind.UnsupportedInput, CognitiveRecoveryAction.AwaitOwner)]
    public void FailureCausePrecedesTaskComplexity(ConversationFailureKind failure, CognitiveRecoveryAction recovery)
    {
        var checkpoint = Checkpoint(6, 3, 3) with { RepositoryStudies = [Guid.NewGuid()], Rounds = new(Cycles: 4, Provider: "chatgpt", LastFailure: failure, StalledCycles: 3, ConfirmedProgress: 3) };
        var decision = CognitiveTaskRoutingPolicy.Evaluate(checkpoint);
        Assert.Equal(CognitiveTaskDemand.Routine, decision.Demand); Assert.Equal(recovery, decision.Recovery); Assert.Equal("chatgpt", decision.ContinuityProvider);
    }
    /// <summary>Failed tools remain a concrete inspection problem even when a complex plan and previous evidence exist.</summary>
    [Fact]
    public void ToolFailureDoesNotProveModelInability()
    {
        var checkpoint = Checkpoint(6, 3, 3); var operations = checkpoint.Operations.ToArray(); operations[^1] = operations[^1] with { Status = "failed" };
        var decision = CognitiveTaskRoutingPolicy.Evaluate(checkpoint with { Operations = operations });
        Assert.Equal(CognitiveRecoveryAction.InspectTool, decision.Recovery); Assert.Equal("gemini", decision.ContinuityProvider);
    }
    /// <summary>Multiple completed rounds without advancement require existing confirmed evidence before cognitive escalation.</summary>
    [Theory]
    [InlineData(2, 2, CognitiveTaskDemand.Analysis)]
    [InlineData(1, 2, CognitiveTaskDemand.Routine)]
    [InlineData(2, 0, CognitiveTaskDemand.Routine)]
    public void EscalationRequiresVerifiedStall(int stalled, int progress, CognitiveTaskDemand expected)
    {
        var checkpoint = Checkpoint(2, 2, progress) with { Rounds = new(Cycles: 4, Provider: "gemini", StalledCycles: stalled, ConfirmedProgress: progress) };
        Assert.Equal(expected, CognitiveTaskRoutingPolicy.Evaluate(checkpoint).Demand);
    }
    /// <summary>Several actually recorded module interpretations support synthesis without claiming that a repository is understood.</summary>
    [Fact]
    public void ActualModuleResultsEnableSynthesisDemand()
    {
        var checkpoint = Checkpoint(2, 2, 2) with { RepositoryStudies = [Guid.NewGuid()] };
        checkpoint = checkpoint with { Operations = checkpoint.Operations.Select(operation => operation with { Tool = "record_repository_analysis" }).ToArray() };
        Assert.Equal(CognitiveTaskDemand.Synthesis, CognitiveTaskRoutingPolicy.Evaluate(checkpoint).Demand);
    }
    /// <summary>Persisted native progress ignores repeated metadata polling and survives subsequent round boundaries.</summary>
    [Fact]
    public async Task PollingCannotManufactureProgress()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); var working = new ConversationWorkingContext(Guid.NewGuid(), "Investigate the synthetic project.");
        var tools = new HashSet<string>(StringComparer.Ordinal) { "plan_task", "recall_memory", "probe", "task_status" };
        await tasks.BeginCycleAsync(working, tools, new(), CancellationToken.None);
        await tasks.ExecuteAsync(working, tools, "plan_task", JsonSerializer.SerializeToElement(new { }), (run, _) =>
        { run.Plan(["recall_memory", "recall_memory", "probe", "probe"]); return Task.FromResult(new Laura.Core.Abstractions.AssistantToolResult("Native plan.")); }, CancellationToken.None);
        for (int sample = 0; sample < 2; sample++)
        { await tasks.ExecuteAsync(working, tools, "recall_memory", JsonSerializer.SerializeToElement(new { sample }), (_, _) => Task.FromResult(new Laura.Core.Abstractions.AssistantToolResult("Actual evidence.")), CancellationToken.None); }
        await tasks.EndCycleAsync(working.TurnId, 1, "gemini", "synthetic", null);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            await tasks.BeginCycleAsync(working, tools, new(), CancellationToken.None);
            await tasks.ExecuteAsync(working, tools, "task_status", JsonSerializer.SerializeToElement(new { }), (_, _) => Task.FromResult(new Laura.Core.Abstractions.AssistantToolResult("Current metadata.")), CancellationToken.None);
            await tasks.EndCycleAsync(working.TurnId, 1, "gemini", "synthetic", null);
        }
        var saved = (await journal.ReadAsync(working.TurnId))!;
        Assert.Equal(2, saved.Rounds!.ConfirmedProgress); Assert.Equal(2, saved.Rounds.StalledCycles);
        Assert.Equal(CognitiveRoutingReason.VerifiedStall, CognitiveTaskRoutingPolicy.Evaluate(saved).Reason);
    }
    /// <summary>Expired idle state cannot refresh its own timestamp by being loaded; explicit owner resumption remains required.</summary>
    [Fact]
    public async Task IdleCheckpointRequiresNativeResume()
    {
        var journal = new RecordingTaskJournal(); var checkpoint = Checkpoint(2, 2, 1) with { Updated = DateTimeOffset.UtcNow.AddHours(-2) };
        await journal.SaveAsync(checkpoint); using var tasks = new CognitiveTasks(journal);
        var tools = new HashSet<string>(StringComparer.Ordinal) { "read_project_file", "run_program" }; var working = new ConversationWorkingContext(checkpoint.Id, checkpoint.Objective);
        Assert.False(await tasks.BeginCycleAsync(working, tools, new() { IdleMinutes = 1 }, CancellationToken.None));
        await tasks.PrepareResumeAsync(checkpoint.Id);
        Assert.True(await tasks.BeginCycleAsync(working, tools, new() { IdleMinutes = 1 }, CancellationToken.None));
    }
    /// <summary>Builds bounded synthetic native metadata; objective wording does not participate in evaluation.</summary>
    private static CognitiveTaskCheckpoint Checkpoint(int steps, int tools, int observed)
    {
        var names = new[] { "read_project_file", "run_program", "file_control" }; DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), "Complex programming debugging analysis synthesis architecture.", now, now, "paused", observed, 32, 0, 600000,
            Enumerable.Range(0, steps).Select(index => new CognitiveTaskStep(index + 1, names[index % tools], "pending")).ToArray(),
            Enumerable.Range(0, observed).Select(index => new CognitiveOperationReceipt(index + 1, names[index % tools], index.ToString("x64", CultureInfo.InvariantCulture), "success", new string('a', 64), false)).ToArray(),
            Rounds: new(Cycles: 2, Provider: "gemini"));
    }
}

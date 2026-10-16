using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Execution;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Engine;

/// <summary>Verifies durable job identities without persisting raw command data or restoring execution authority.</summary>
public sealed class CommandJobTaskTests
{
    private static readonly HashSet<string> Tools = ["start_program", "program_status", "stop_program"];

    /// <summary>Background effects cannot outlive an unattributed temporary task that would lose its ownership record.</summary>
    [Fact]
    public async Task UnattributedBackgroundExecutionIsBlockedBeforeDispatch()
    {
        using var tasks = new CognitiveTasks(new RecordingTaskJournal());
        var result = await tasks.ExecuteAsync(new ConversationWorkingContext(Guid.Empty, ""), Tools, "start_program", JsonSerializer.SerializeToElement(new { }),
            (_, _) => throw new InvalidOperationException("Unattributed command dispatched."), CancellationToken.None);
        Assert.Equal("blocked", result.Status); Assert.Empty(await tasks.ListAsync());
    }

    /// <summary>A verified start retains only its native job identity; recovery cannot replay the effect or attach a PID.</summary>
    [Fact]
    public async Task JobIdentitySurvivesCheckpointWithoutRawOutputOrReplay()
    {
        var journal = new RecordingTaskJournal(); Guid id = Guid.NewGuid(), jobId = Guid.NewGuid();
        var run = new CognitiveTaskRun("Observe a bounded command.", Tools, taskId: id, journal: journal);
        var arguments = JsonSerializer.SerializeToElement(new { executable = "private-entry.exe", arguments = "private-argument" });
        await run.ExecuteAsync("start_program", arguments, _ => Task.FromResult(new AssistantToolResult("private-output") { CommandJobId = jobId }), CancellationToken.None);
        var checkpoint = (await journal.ReadAsync(id))!;
        Assert.Equal(jobId, Assert.Single(checkpoint.CommandJobs!)); Assert.False(checkpoint.HasUncertainEffect);
        string stored = JsonSerializer.Serialize(checkpoint); Assert.DoesNotContain("private-", stored, StringComparison.Ordinal);
        var restored = new CognitiveTaskRun(checkpoint.Objective, Tools, taskId: id, restored: checkpoint);
        var replay = await restored.ExecuteAsync("start_program", arguments, _ => throw new InvalidOperationException("Effect replayed."), CancellationToken.None);
        Assert.Equal("blocked", replay.Status); Assert.Contains(jobId.ToString(), checkpoint.ResumePrompt(), StringComparison.Ordinal);
        await restored.ExecuteAsync("program_status", JsonSerializer.SerializeToElement(new { id = jobId }), _ => Task.FromResult(new AssistantToolResult("Unavailable in this process session.")), CancellationToken.None);
        Assert.False(restored.Checkpoint().Operations[^1].MayHaveEffects);
        Assert.Equal(jobId, Assert.Single(restored.Checkpoint().CommandJobs!));
    }

    /// <summary>Task cancellation also reaches detached jobs, while deletion refuses to discard active ownership records.</summary>
    [Fact]
    public async Task NativeTaskCancellationStopsDetachedJobsBeforeDeletion()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor()); var journal = new RecordingTaskJournal();
        using var tasks = new CognitiveTasks(journal, jobs); Guid id = Guid.NewGuid();
        var arguments = JsonSerializer.SerializeToElement(new { executable = "fixture.exe" }); Guid jobId = Guid.Empty;
        await tasks.ExecuteAsync(new ConversationWorkingContext(id, "Observe the synthetic job."), Tools, "start_program", arguments, async (_, token) =>
        {
            var job = await jobs.StartAsync(new(Path.GetFullPath("fixture.exe"), [], Path.GetFullPath(".")) { TaskId = id }, "", token);
            jobId = job.Id; return new AssistantToolResult("Started; goal unverified.") { CommandJobId = job.Id };
        }, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tasks.DeleteAsync(id)); Assert.NotNull(await journal.ReadAsync(id));
        tasks.Cancel(id); await jobs.StopAsync(id, jobId, ""); await tasks.DeleteAsync(id);
        Assert.Empty(jobs.List(id)); Assert.Null(await journal.ReadAsync(id));
    }

    /// <summary>Per-task identity retention is bounded before another command can dispatch.</summary>
    [Fact]
    public async Task JobRetentionLimitPrecedesDispatch()
    {
        var run = new CognitiveTaskRun("Observe bounded jobs.", Tools); int dispatched = 0;
        for (int i = 0; i < 17; i++)
        {
            var result = await run.ExecuteAsync("start_program", JsonSerializer.SerializeToElement(new { index = i }), _ =>
            { dispatched++; return Task.FromResult(new AssistantToolResult("Started.") { CommandJobId = Guid.NewGuid() }); }, CancellationToken.None);
            Assert.Equal(i == 16, result.Failed);
        }
        Assert.Equal(16, dispatched); Assert.Equal(16, run.Checkpoint().CommandJobs!.Count);
        var checkpoint = run.Checkpoint();
        Assert.Throws<InvalidDataException>(() => (checkpoint with { CommandJobs = [Guid.Empty] }).Validated());
        Assert.Throws<InvalidDataException>(() => (checkpoint with { CommandJobs = [checkpoint.CommandJobs![0], checkpoint.CommandJobs[0]] }).Validated());
    }
}

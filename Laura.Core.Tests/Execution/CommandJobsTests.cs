using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Execution;

/// <summary>Verifies process-session ownership, bounded observations and cancellation independently of native launch details.</summary>
public sealed class CommandJobsTests
{
    /// <summary>Creates portable absolute fixture paths and a trusted owner identity. Returns: Frozen-domain input.</summary>
    private static CommandExecutionRequest Request(Guid? owner = null) => new(Path.GetFullPath("fixture.exe"), ["literal"], Path.GetFullPath(".")) { TaskId = owner ?? Guid.NewGuid() };

    /// <summary>Native creation, rather than scheduling a task or receiving approval, releases start admission.</summary>
    [Fact]
    public async Task StartWaitsForActualCreation()
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var create = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeCommandExecutor { Handler = async (request, _, started, token) =>
        {
            Assert.True(request.Background); admitted.SetResult(); await create.Task.WaitAsync(token); started!(42);
            await Task.Delay(Timeout.Infinite, token); return new(42, 0, "", "", "exited", false, TimeSpan.Zero);
        } };
        await using var jobs = new CommandJobs(fake); var request = Request();
        Task<CommandJobSnapshot> pending = jobs.StartAsync(request, ""); await admitted.Task;
        Assert.False(pending.IsCompleted); Assert.Equal("awaiting_approval", Assert.Single(jobs.List(request.TaskId)).Status);
        create.SetResult(); var running = await pending;
        Assert.Equal(42, running.ProcessId); Assert.Equal("running", running.Status); Assert.False(running.Succeeded);
    }

    /// <summary>The caller's completed tool lifetime cannot cancel an admitted background job.</summary>
    [Fact]
    public async Task CallerCancellationAfterStartDoesNotStopJob()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor()); using var caller = new CancellationTokenSource(); var request = Request();
        var job = await jobs.StartAsync(request, "", caller.Token); await caller.CancelAsync();
        Assert.Equal("running", jobs.Read(request.TaskId, job.Id, "").Status);
        Assert.Equal("cancelled", (await jobs.StopAsync(request.TaskId, job.Id, "")).Status);
    }

    /// <summary>Pending admission remains cancellable and never reports an invented process.</summary>
    [Fact]
    public async Task CancellationDuringAdmissionDoesNotReportStarted()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var jobs = new CommandJobs(new FakeCommandExecutor { Handler = async (_, _, _, token) =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable fixture."); } });
        using var cancellation = new CancellationTokenSource(); var request = Request();
        var pending = jobs.StartAsync(request, "", cancellation.Token); await entered.Task; await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var observed = Assert.Single(jobs.List(request.TaskId)); Assert.Null(observed.ProcessId); Assert.Equal("cancelled", observed.Status);
    }

    /// <summary>Exact denial propagates and a final result without a creation notification is refused.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DenialOrMissingCreationCannotBecomeRunning(bool denied)
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor { Handler = (_, _, _, _) => denied
            ? Task.FromException<CommandExecutionResult>(new PermissionDeniedException("Synthetic denial."))
            : Task.FromResult(new CommandExecutionResult(42, 0, "", "", "exited", false, TimeSpan.Zero)) });
        var request = Request(); await Assert.ThrowsAnyAsync<Exception>(() => jobs.StartAsync(request, ""));
        var job = Assert.Single(jobs.List(request.TaskId)); Assert.Null(job.ProcessId); Assert.Equal(denied ? "blocked" : "failed", job.Status);
    }

    /// <summary>Only the native owner and original disclosure recipient can read or stop a job.</summary>
    [Fact]
    public async Task ForeignTaskAndChangedRecipientAreRefused()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor()); var request = Request(); var job = await jobs.StartAsync(request, "api.openai.com");
        Assert.Throws<InvalidOperationException>(() => jobs.Read(Guid.NewGuid(), job.Id, "api.openai.com"));
        Assert.Throws<InvalidOperationException>(() => jobs.Read(request.TaskId, job.Id, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.StopAsync(request.TaskId, job.Id, "other.example"));
        Assert.Empty(Assert.Single(jobs.List(request.TaskId)).Output); Assert.Equal("running", jobs.Read(request.TaskId, job.Id, "api.openai.com").Status);
    }

    /// <summary>Stopping one task preserves other owned jobs and forbids forgetting an active observation.</summary>
    [Fact]
    public async Task TaskCancellationAndRetentionRemainIndependent()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor()); var first = Request(); var second = Request();
        var a = await jobs.StartAsync(first, ""); var b = await jobs.StartAsync(second, "");
        Assert.Throws<InvalidOperationException>(() => jobs.ForgetTask(first.TaskId));
        jobs.CancelTask(first.TaskId); await jobs.StopAsync(first.TaskId, a.Id, ""); jobs.ForgetTask(first.TaskId);
        Assert.Empty(jobs.List(first.TaskId)); Assert.Equal("running", jobs.Read(second.TaskId, b.Id, "").Status);
    }

    /// <summary>Cursor pages expose retention gaps and preserve stream provenance without unbounded arrays.</summary>
    [Fact]
    public async Task OutputPagesAreBoundedAndReportGaps()
    {
        var emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var jobs = new CommandJobs(new FakeCommandExecutor { Handler = async (_, progress, started, token) =>
        {
            started!(42); for (int i = 1; i <= 300; i++) { progress!.Report(new(i, i % 2 == 0, new string('x', 600))); }
            emitted.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable fixture.");
        } });
        var request = Request(); var job = await jobs.StartAsync(request, ""); await emitted.Task;
        var page = jobs.Read(request.TaskId, job.Id, ""); Assert.Equal(32, page.Output.Count); Assert.True(page.OutputLost); Assert.Equal(300, page.LatestSequence);
        Assert.Contains(page.Output, item => item.StandardError); Assert.Contains(page.Output, item => !item.StandardError);
        var next = jobs.Read(request.TaskId, job.Id, "", page.NextSequence); Assert.False(next.OutputLost); Assert.True(next.NextSequence > page.NextSequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => jobs.Read(request.TaskId, job.Id, "", -1));
    }

    /// <summary>Native nonzero exit is observed as process failure, and an invalid observer cannot change it.</summary>
    [Fact]
    public async Task NonzeroExitAndObserverFailureRemainSeparate()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor { Handler = (_, progress, started, _) =>
        { started!(42); progress!.Report(new(1, false, new string('x', 5000))); return Task.FromResult(new CommandExecutionResult(42, 7, "", "", "exited", false, TimeSpan.FromSeconds(1))); } });
        jobs.Changed += (_, _) => throw new InvalidOperationException("Synthetic view failure.");
        var request = Request(); var job = await jobs.StartAsync(request, ""); var result = await jobs.StopAsync(request.TaskId, job.Id, "");
        Assert.True(result.Finished); Assert.False(result.Succeeded); Assert.Equal(7, result.ExitCode); Assert.True(result.Truncated);
        Assert.Equal(1024, Assert.Single(result.Output).Text.Length);
    }

    /// <summary>Admission bounds concurrent native lifetimes and rejects missing ownership or PID-based reattachment.</summary>
    [Fact]
    public async Task ActiveLimitAndUnknownIdentityFailClosed()
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor()); var request = Request();
        for (int i = 0; i < 4; i++) { await jobs.StartAsync(request, ""); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.StartAsync(request, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.StartAsync(request with { TaskId = Guid.Empty }, ""));
        Assert.Throws<InvalidOperationException>(() => jobs.Read(request.TaskId, Guid.NewGuid(), ""));
    }

    /// <summary>Shutdown is idempotent, observes every worker and blocks new execution.</summary>
    [Fact]
    public async Task ShutdownStopsAllOwnedJobs()
    {
        var jobs = new CommandJobs(new FakeCommandExecutor()); var request = Request(); var job = await jobs.StartAsync(request, "");
        await jobs.DisposeAsync(); await jobs.DisposeAsync();
        Assert.Equal("cancelled", jobs.Read(request.TaskId, job.Id, "").Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => jobs.StartAsync(request, ""));
    }

    /// <summary>An adapter cannot change its observed native identity or inject an unrecognized terminal state.</summary>
    [Theory]
    [InlineData(43, "exited")]
    [InlineData(42, "invented")]
    public async Task InconsistentCompletionFailsClosed(int returnedId, string status)
    {
        await using var jobs = new CommandJobs(new FakeCommandExecutor { Handler = (_, _, started, _) =>
        { started!(42); return Task.FromResult(new CommandExecutionResult(returnedId, 0, "", "", status, false, TimeSpan.Zero)); } });
        var request = Request(); var job = await jobs.StartAsync(request, "");
        var final = await jobs.StopAsync(request.TaskId, job.Id, ""); Assert.Equal("failed", final.Status); Assert.Equal(42, final.ProcessId); Assert.False(final.Succeeded);
    }
}

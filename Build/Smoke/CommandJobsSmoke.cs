using System.Diagnostics;
using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;

/// <summary>Verifies real background process ownership and shutdown independently of UI or inference.</summary>
internal static class CommandJobsSmoke
{
    /// <summary>Starts only synthetic SDK-hosted fixtures and verifies actual exit, bounded streams, exact denial and exclusive cleanup.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        using var provider = new ProviderExecutionScope("local"); bool approve = true;
        using var permissions = new PermissionManager(new PermissionStore(Path.Combine(directory, "Security", "permissions.json")), () => new());
        permissions.Changed += (_, _) => { foreach (var pending in permissions.PendingRequests) { permissions.Decide(pending.Id, approve ? ApprovalChoice.Once : ApprovalChoice.Deny); } };
        await using var jobs = new CommandJobs(new WindowsCommandExecutor(permissions));
        string assembly = typeof(CommandSmoke).Assembly.Location;
        var request = new CommandExecutionRequest(Environment.ProcessPath!, [assembly, "--process-fixture", "wait"], directory) { TaskId = Guid.NewGuid(), TimeoutSeconds = 30 };
        var running = await jobs.StartAsync(request, "", cancellationToken);
        using var owned = Process.GetProcessById(running.ProcessId!.Value); _ = owned.Handle;
        var failed = await jobs.StartAsync(request with { TaskId = Guid.NewGuid(), Arguments = [assembly, "--process-fixture", "fail", "literal argument"] }, "", cancellationToken);
        var failure = await FinishedAsync(failed).ConfigureAwait(false);
        if (failure.ExitCode != 7 || failure.Succeeded || failure.Output.All(chunk => !chunk.StandardError) || !jobs.Read(request.TaskId, running.Id, "").Status.Equals("running", StringComparison.Ordinal))
        { throw new InvalidOperationException("Background completion, stream provenance or independent progress failed."); }
        var timed = await jobs.StartAsync(request with { TaskId = Guid.NewGuid(), TimeoutSeconds = 1 }, "", cancellationToken);
        if ((await FinishedAsync(timed).ConfigureAwait(false)).Status != "timeout") { throw new InvalidOperationException("Background timeout did not observe native termination."); }
        var stopped = await jobs.StopAsync(request.TaskId, running.Id, "").ConfigureAwait(false);
        if (stopped.Status != "cancelled" || !owned.HasExited) { throw new InvalidOperationException("Scoped stop did not terminate the retained native process."); }
        approve = false;
        try { await jobs.StartAsync(request with { TaskId = Guid.NewGuid() }, "", cancellationToken); throw new InvalidOperationException("Denied background process started."); }
        catch (PermissionDeniedException) { }
        approve = true;
        var tree = await jobs.StartAsync(request with { TaskId = Guid.NewGuid(), Arguments = [assembly, "--process-fixture", "tree-wait"] }, "", cancellationToken);
        CommandJobSnapshot page;
        do { await Task.Delay(25, cancellationToken); page = jobs.Read(tree.TaskId, tree.Id, ""); } while (!page.Output.Any(chunk => chunk.Text.Contains('\n')));
        string childText = string.Concat(page.Output.Select(chunk => chunk.Text));
        using var child = Process.GetProcessById(int.Parse(childText[12..].Trim(), global::System.Globalization.CultureInfo.InvariantCulture)); _ = child.Handle;
        using var parent = Process.GetProcessById(tree.ProcessId!.Value); _ = parent.Handle;
        var unrelatedStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { assembly, "--process-fixture", "wait" }) { unrelatedStart.ArgumentList.Add(argument); }
        using var unrelated = Process.Start(unrelatedStart)!; _ = unrelated.Handle;
        try
        {
            await jobs.DisposeAsync().ConfigureAwait(false);
            if (!child.HasExited || !parent.HasExited || unrelated.HasExited) { throw new InvalidOperationException("Shutdown did not preserve exclusive job ownership."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "command-jobs-runtime.json"), JsonSerializer.Serialize(new { ReturnedWhileRunning = true, IndependentNonzeroExit = true,
                SeparateOutputStreams = true, ScopedStop = true, ExactDenial = true, Timeout = true, ShutdownClosedParentAndChild = true, IndependentProcessPreserved = true }), cancellationToken);
        }
        finally { if (!unrelated.HasExited) { unrelated.Kill(entireProcessTree: true); await unrelated.WaitForExitAsync(CancellationToken.None); } }
        Console.WriteLine("Native background command smoke passed: observed creation, independent progress, output, exit 7, denial, timeout, scoped stop, shutdown tree cleanup and independent process preservation.");

        // Polling is confined to this diagnostic, never an unbounded model continuation.
        async Task<CommandJobSnapshot> FinishedAsync(CommandJobSnapshot job)
        {
            long began = Stopwatch.GetTimestamp(); CommandJobSnapshot current;
            while (!(current = jobs.Read(job.TaskId, job.Id, "")).Finished)
            { if (Stopwatch.GetElapsedTime(began) > TimeSpan.FromSeconds(8)) { throw new TimeoutException("Synthetic command did not finish."); } await Task.Delay(25, cancellationToken); }
            return current;
        }
    }
}

using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;

/// <summary>Exercises actual Windows file and PowerShell effects under all three native permission modes.</summary>
internal static class PermissionAutonomySmoke
{
    /// <summary>Runs isolated effects and writes factual results. Args: directory: Fresh diagnostic root; cancellationToken: Overall deadline.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        string workspace = Path.Combine(directory, "Workspace");
        string security = Path.Combine(directory, "Security");
        Directory.CreateDirectory(workspace);
        ApprovalMode mode = ApprovalMode.Automatic;
        using var permissions = new PermissionManager(new PermissionStore(Path.Combine(security, "permission-autonomy.json")),
            () => new PermissionOptions { Mode = mode }, () => [security], workspace);
        Guid taskId = Guid.NewGuid();
        string code = Path.Combine(workspace, "Program.cs");
        await permissions.ExecuteAsync(new(PermissionOperation.FileWrite, "native file adapter", "Create a task source file", [code], taskId: taskId),
            async token => { await File.WriteAllTextAsync(code, "class ProgramFixture {}", token); return true; }, cancellationToken);
        if (await File.ReadAllTextAsync(code, cancellationToken) != "class ProgramFixture {}" || permissions.PendingRequests.Count != 0)
        { throw new InvalidOperationException("Automatic task file creation failed or requested approval."); }

        string outside = Path.Combine(directory, "Outside.cs");
        Task outsideWrite = permissions.ExecuteAsync(new(PermissionOperation.FileWrite, "native file adapter", "Outside task scope", [outside], taskId: taskId),
            async token => { await File.WriteAllTextAsync(outside, "must not write", token); return true; }, cancellationToken);
        permissions.Decide(AssertPending(permissions).Id, ApprovalChoice.Deny);
        try { await outsideWrite; throw new InvalidOperationException("Outside task scope ran without approval."); }
        catch (PermissionDeniedException) { }
        if (File.Exists(outside)) { throw new InvalidOperationException("Denied outside file was created."); }

        mode = ApprovalMode.Ask;
        string reviewed = Path.Combine(workspace, "Reviewed.cs");
        Task reviewedWrite = permissions.ExecuteAsync(new(PermissionOperation.FileWrite, "native file adapter", "Reviewed source", [reviewed], taskId: taskId),
            async token => { await File.WriteAllTextAsync(reviewed, "class ReviewedFixture {}", token); return true; }, cancellationToken);
        if (File.Exists(reviewed)) { throw new InvalidOperationException("Ask mode dispatched before review."); }
        permissions.Decide(AssertPending(permissions).Id, ApprovalChoice.Once);
        await reviewedWrite;
        if (await File.ReadAllTextAsync(reviewed, cancellationToken) != "class ReviewedFixture {}") { throw new InvalidOperationException("Reviewed file write failed."); }

        mode = ApprovalMode.FullAccess;
        if (permissions.CurrentMode != ApprovalMode.Ask) { throw new InvalidOperationException("Full access activated from a saved setting without native activation."); }
        permissions.ActivateFullAccess();
        using var provider = new ProviderExecutionScope("local");
        var executor = new WindowsCommandExecutor(permissions);
        string executable = WindowsCommandEnvironment.ResolvePowerShell();
        var command = new CommandExecutionRequest(executable, ["-Command", "[IO.File]::WriteAllText('full.txt','full')"], workspace) { TaskId = taskId };
        CommandExecutionResult process = await executor.ExecuteAsync(command, cancellationToken: cancellationToken);
        if (!process.Succeeded || await File.ReadAllTextAsync(Path.Combine(workspace, "full.txt"), cancellationToken) != "full"
            || permissions.PendingRequests.Count != 0 || process.Authorization != "full_access")
        { throw new InvalidOperationException("Full access did not run or verify the real PowerShell child."); }
        permissions.DeactivateFullAccess();
        Task<CommandExecutionResult> revokedProcess = executor.ExecuteAsync(command with
        { Arguments = ["-Command", "[IO.File]::WriteAllText('revoked.txt','must not write')"] }, cancellationToken: cancellationToken);
        permissions.Decide(await WaitPendingAsync(permissions, cancellationToken), ApprovalChoice.Deny);
        try { await revokedProcess; throw new InvalidOperationException("Revoked full access launched another child."); }
        catch (PermissionDeniedException) { }
        if (File.Exists(Path.Combine(workspace, "revoked.txt"))) { throw new InvalidOperationException("Revoked PowerShell command had an effect."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "permission-autonomy-result.json"), JsonSerializer.Serialize(new
        {
            automaticTaskCodeWritten = true, outOfScopeDenied = true, askWaitedForApproval = true,
            fullAccessPowerShellExitCode = process.ExitCode, fullAccessAuthorization = process.Authorization,
            deactivationStoppedNextProcess = true, auditOutcomes = permissions.History.Select(item => item.Outcome.ToString()).ToArray()
        }), cancellationToken);
        Console.WriteLine("Native permission autonomy smoke passed: real task code write, out-of-scope denial, Ask review, full-access PowerShell effect and immediate revocation.");
    }

    /// <summary>Returns the single synchronous review request. Raises: InvalidOperationException when review was bypassed.</summary>
    private static ApprovalRequest AssertPending(PermissionManager permissions) => permissions.PendingRequests.Single();

    /// <summary>Waits for an asynchronous process hash and its native approval request.</summary>
    private static async Task<Guid> WaitPendingAsync(PermissionManager permissions, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (permissions.PendingRequests.Count == 0) { await Task.Delay(20, timeout.Token); }
        return permissions.PendingRequests.Single().Id;
    }
}

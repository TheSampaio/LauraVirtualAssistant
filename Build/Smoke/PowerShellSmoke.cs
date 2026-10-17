using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;

/// <summary>Verifies explicit PowerShell execution through actual central approval using only synthetic workspace scripts.</summary>
internal static class PowerShellSmoke
{
    /// <summary>Exercises visible commands, pinned scripts, denial, literal arguments, nonzero exits and bounded lifecycle behavior.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        using var provider = new ProviderExecutionScope("local"); string executable = WindowsCommandEnvironment.ResolvePowerShell();
        string root = Path.Combine(directory, "PowerShell workspace ação 漢字"); Directory.CreateDirectory(root);
        string script = Path.Combine(root, "Entry script.ps1"), sentinel = Path.Combine(root, "must-not-exist.txt");
        await File.WriteAllTextAsync(script, "param([string]$Value)\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n[Console]::Write($Value)\n[Console]::Error.Write('synthetic stderr')\nexit 7\n", cancellationToken);
        using var permissions = new PermissionManager(new PermissionStore(Path.Combine(directory, "Security", "powershell-permissions.json")), () => new());
        bool approve = true, entryLocked = false, pinShown = false;
        permissions.Changed += (_, _) =>
        {
            foreach (var review in permissions.PendingRequests)
            {
                if (review.Action.Resources.Contains(script))
                {
                    pinShown |= review.Action.Description.Contains("Entry script SHA-256:", StringComparison.Ordinal);
                    try { using var probe = new FileStream(script, FileMode.Open, FileAccess.Write, FileShare.Read); }
                    catch (IOException) { entryLocked = true; }
                }
                permissions.Decide(review.Id, approve ? ApprovalChoice.Once : ApprovalChoice.Deny);
            }
        };
        var executor = new WindowsCommandExecutor(permissions);
        var command = new CommandExecutionRequest(executable, ["-Command", "[Console]::Write('verified')"], root);
        var observed = await executor.ExecuteAsync(command, cancellationToken: cancellationToken);
        if (!observed.Succeeded || observed.StandardOutput != "verified") { throw new InvalidOperationException("Native explicit PowerShell command failed."); }
        const string literal = "a b ação 漢字 $(Remove-Item *)";
        observed = await executor.ExecuteAsync(command with { Arguments = ["-File", script, literal] }, cancellationToken: cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "powershell-script-observation.json"), JsonSerializer.Serialize(new { observed, entryLocked, pinShown }), cancellationToken);
        if (observed.Succeeded || observed.ExitCode != 7 || observed.StandardOutput != literal || observed.StandardError != "synthetic stderr" || !entryLocked || !pinShown)
        { throw new InvalidOperationException("Pinned script, literal arguments, stderr or nonzero outcome failed."); }
        approve = false;
        try { await executor.ExecuteAsync(command with { Arguments = ["-Command", "[IO.File]::WriteAllText('must-not-exist.txt', 'forbidden')"] }, cancellationToken: cancellationToken); throw new InvalidOperationException("Denied script executed."); }
        catch (PermissionDeniedException) { }
        if (File.Exists(sentinel)) { throw new InvalidOperationException("Denied PowerShell mutated its workspace."); }
        try { await executor.ExecuteAsync(command with { Arguments = ["-EncodedCommand", "YQ=="] }, cancellationToken: cancellationToken); throw new InvalidOperationException("Opaque host mode was accepted."); }
        catch (ArgumentException) { }
        approve = true;
        observed = await executor.ExecuteAsync(command with { Arguments = ["-Command", "[Console]::Write(('x' * 200000))"], OutputCharacters = 4096 }, cancellationToken: cancellationToken);
        if (!observed.Succeeded || !observed.Truncated || observed.StandardOutput.Length != 4096) { throw new InvalidOperationException("PowerShell output was not bounded/drained."); }
        var waiting = command with { Arguments = ["-Command", "[Threading.Thread]::Sleep(30000)"], TimeoutSeconds = 1 };
        observed = await executor.ExecuteAsync(waiting, cancellationToken: cancellationToken);
        if (observed.Status != "timeout" || observed.Succeeded) { throw new InvalidOperationException("PowerShell timeout was not observed."); }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cancel.CancelAfter(500);
        observed = await executor.ExecuteAsync(waiting with { TimeoutSeconds = 30 }, cancellationToken: cancel.Token);
        if (observed.Status != "cancelled" || observed.Succeeded) { throw new InvalidOperationException("PowerShell cancellation was not observed."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "powershell-result.json"), JsonSerializer.Serialize(new { profile = false, interactive = false, encodedCommands = false,
            entryScriptPinned = entryLocked && pinShown, literalUnicodeArguments = true, exitSevenObserved = true, separateStderr = true, denialPreventedWrite = true, boundedOutput = true, timeout = true, cancellation = true, sandbox = false }), cancellationToken);
        Console.WriteLine("Native PowerShell smoke passed: explicit command, pinned entry script, literal Unicode/space/shell-looking arguments, stderr, exit 7, denial, opaque-mode rejection, output bounds, timeout and cancellation. No OS sandbox is claimed.");
    }
}

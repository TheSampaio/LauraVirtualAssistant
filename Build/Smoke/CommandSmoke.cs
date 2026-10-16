using System.Collections.Concurrent;
using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;

/// <summary>Opt-in native process verification with synthetic child programs and isolated permissions.</summary>
internal static class CommandSmoke
{
    /// <summary>Runs a bounded fixture child without accessing user files or credentials.</summary>
    internal static int RunFixture(string[] arguments)
    {
        if (arguments[0] == "wait") { Thread.Sleep(30_000); return 0; }
        if (arguments[0] == "large") { Console.Write(new string('x', 200_000)); Console.Error.Write(new string('y', 200_000)); return 0; }
        Console.OutputEncoding = global::System.Text.Encoding.UTF8;
        Console.Write(JsonSerializer.Serialize(new { arguments = arguments[1..], directory = Environment.CurrentDirectory, inheritedSecret = Environment.GetEnvironmentVariable("LAURA_SMOKE_SECRET") }));
        Console.Error.Write("synthetic stderr");
        return arguments[0] == "fail" ? 7 : 0;
    }

    /// <summary>Verifies literal arguments, bounded concurrent streams, exit status, timeout, cancellation and denial through the actual executor.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        using var provider = new ProviderExecutionScope("local");
        string workspace = Path.Combine(directory, "Project with spaces ação 漢字"); Directory.CreateDirectory(workspace);
        using var permissions = new PermissionManager(new PermissionStore(Path.Combine(directory, "Security", "process-permissions.json")), () => new());
        bool approve = true;
        permissions.Changed += (_, _) => { foreach (var request in permissions.PendingRequests) { permissions.Decide(request.Id, approve ? ApprovalChoice.Once : ApprovalChoice.Deny); } };
        var executor = new WindowsCommandExecutor(permissions); var progress = new OutputRecorder();
        string assembly = typeof(CommandSmoke).Assembly.Location;
        var request = new CommandExecutionRequest(Environment.ProcessPath!, [assembly, "--process-fixture", "echo", "a b", "ação 漢字", "$(Remove-Item *)"], workspace);
        Environment.SetEnvironmentVariable("LAURA_SMOKE_SECRET", "synthetic-secret-must-not-be-inherited");
        try
        {
            CommandExecutionResult echoed = await executor.ExecuteAsync(request, progress, cancellationToken);
            if (!echoed.Succeeded || echoed.StandardError != "synthetic stderr" || echoed.ProcessId is null) { throw new InvalidOperationException("Native process exit or stream verification failed."); }
            using var result = JsonDocument.Parse(echoed.StandardOutput);
            if (result.RootElement.GetProperty("arguments")[0].GetString() != "a b" || result.RootElement.GetProperty("arguments")[1].GetString() != "ação 漢字"
                || result.RootElement.GetProperty("arguments")[2].GetString() != "$(Remove-Item *)" || result.RootElement.GetProperty("directory").GetString() != workspace
                || result.RootElement.GetProperty("inheritedSecret").ValueKind != JsonValueKind.Null)
            { throw new InvalidOperationException("Literal arguments, Unicode, workspace or controlled environment failed."); }
            var failed = await executor.ExecuteAsync(request with { Arguments = [assembly, "--process-fixture", "fail"] }, cancellationToken: cancellationToken);
            if (failed.Succeeded || failed.ExitCode != 7) { throw new InvalidOperationException("Nonzero native exit was reported as success."); }
            var large = await executor.ExecuteAsync(request with { Arguments = [assembly, "--process-fixture", "large"], OutputCharacters = 4096 }, cancellationToken: cancellationToken);
            if (!large.Truncated || large.StandardOutput.Length + large.StandardError.Length != 4096 || !large.Succeeded) { throw new InvalidOperationException("Large process output was not bounded or drained."); }
            var waiting = request with { Arguments = [assembly, "--process-fixture", "wait"], TimeoutSeconds = 1 };
            var timedOut = await executor.ExecuteAsync(waiting, cancellationToken: cancellationToken);
            if (timedOut.Status != "timeout" || timedOut.Succeeded || timedOut.ExitCode is null) { throw new InvalidOperationException("Timeout did not terminate the owned process."); }
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cancel.CancelAfter(500);
            var cancelled = await executor.ExecuteAsync(waiting with { TimeoutSeconds = 30 }, cancellationToken: cancel.Token);
            if (cancelled.Status != "cancelled" || cancelled.Succeeded || cancelled.ExitCode is null) { throw new InvalidOperationException("Cancellation did not terminate the owned process."); }
            approve = false;
            try { await executor.ExecuteAsync(request, cancellationToken: cancellationToken); throw new InvalidOperationException("A denied process ran."); }
            catch (PermissionDeniedException) { }
            if (progress.Items.Count == 0) { throw new InvalidOperationException("Process streaming emitted no progress."); }
            Console.WriteLine("Native command smoke passed: exact approval/denial, spaces/Unicode, literal shell syntax, controlled environment, separate stdout/stderr, exit 7, large output, timeout, cancellation and cleanup.");
        }
        finally { Environment.SetEnvironmentVariable("LAURA_SMOKE_SECRET", null); }
    }

    /// <summary>Captures bounded native stream callbacks synchronously without a UI dispatcher.</summary>
    private sealed class OutputRecorder : IProgress<CommandOutput>
    {
        internal ConcurrentQueue<CommandOutput> Items { get; } = new();
        /// <inheritdoc />
        public void Report(CommandOutput value) => Items.Enqueue(value);
    }
}

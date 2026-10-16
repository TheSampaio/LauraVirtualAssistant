using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;
using Laura.Core.Execution;
using Laura.Core.Permissions;

namespace Laura.Platform.Windows.System;

/// <summary>Runs exact reviewed processes without shell interpolation, inherited input, policy bypass or automatic command grants.</summary>
public sealed class WindowsCommandExecutor(PermissionManager permissions) : ICommandExecutor
{
    private static readonly string[] EnvironmentNames = ["SystemRoot", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "COMSPEC"];

    /// <inheritdoc />
    public async Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress = null, Action<int>? started = null, CancellationToken cancellationToken = default)
    {
        var invocation = PowerShellInvocation.Prepare(request); request = invocation.Request;
        PermissionPolicy.ValidateResources(new(PermissionOperation.Process, "Native command executor", "Validate process resources before metadata IO",
            invocation.ScriptPath is { } selectedScript ? [request.Executable, request.WorkingDirectory, selectedScript] : [request.Executable, request.WorkingDirectory]), []);
        if (!File.Exists(request.Executable) || !Directory.Exists(request.WorkingDirectory)) { throw new FileNotFoundException("The executable or working directory is unavailable."); }
        FileStream? script = null;
        string? scriptHash = null;
        if (invocation.ScriptPath is { } scriptPath)
        {
            PermissionPolicy.ValidateResources(new(PermissionOperation.Process, "Native command executor", "Validate script identity", [scriptPath]), []);
            script = new FileStream(scriptPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        }
        await using var scriptLifetime = script;
        if (script is not null) { scriptHash = Convert.ToHexString(await SHA256.HashDataAsync(script, cancellationToken).ConfigureAwait(false)); }
        // Keep executable bytes stable from hashing through native dispatch on Windows.
        await using var image = new FileStream(request.Executable, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(image, cancellationToken).ConfigureAwait(false));
        var action = new PermissionAction(PermissionOperation.Process, "Native command executor",
            request.Executable + "\n" + JsonSerializer.Serialize(request.Arguments) + "\n" + request.WorkingDirectory + (scriptHash is null ? "" : "\nEntry script SHA-256: " + scriptHash)
                + "\nWorking directory does not restrict process filesystem or network access. Entry-script identity does not verify imported modules or nested scripts."
                + (request.Background ? "\nRun in the background until the reviewed timeout, task cancellation or application shutdown. Owned descendants stop with this command." : ""),
            invocation.ScriptPath is { } entry ? [request.Executable, request.WorkingDirectory, entry] : [request.Executable, request.WorkingDirectory],
            JsonSerializer.Serialize(new { request.Arguments, request.TimeoutSeconds, request.OutputCharacters, request.Background, hash, scriptHash }),
            ProviderExecutionScope.Destination, taskId: request.TaskId);
        return await permissions.ExecuteAsync(action, token => RunAsync(request, progress, started, token), result => result.Succeeded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drains both streams even after retention is exhausted and closes the atomically assigned native job on every exit path.</summary>
    private static async Task<CommandExecutionResult> RunAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress, Action<int>? started, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in EnvironmentNames) { if (Environment.GetEnvironmentVariable(name) is { } value) { environment[name] = value; } }
        environment["DOTNET_NOLOGO"] = "1"; environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        long began = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        using var process = OwnedCommandProcess.Start(request, environment);
        int processId = process.Id;
        started?.Invoke(processId);
        var stdout = new StringBuilder(); var stderr = new StringBuilder(); object gate = new(); long sequence = 0;
        int retained = 0; bool truncated = false;
        Task output = DrainAsync(process.StandardOutput, stdout, false), error = DrainAsync(process.StandardError, stderr, true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        string status = "exited";
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            status = cancellationToken.IsCancellationRequested ? "cancelled" : "timeout";
            try { process.Terminate(); }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException) { status = "termination_failed"; }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (TimeoutException) { status = "termination_failed"; }
        }
        // A successful initial process can leave children holding pipes or doing work. They remain exclusively owned.
        try { process.Terminate(); await process.WaitForEmptyAsync().ConfigureAwait(false); }
        catch (Exception failure) when (failure is Win32Exception or TimeoutException) { status = "termination_failed"; }
        try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or TimeoutException)
        { process.StandardOutput.Close(); process.StandardError.Close(); status = "output_incomplete"; }
        TimeSpan elapsed = Stopwatch.GetElapsedTime(began);
        LauraDiagnostics.Record("process.completed", elapsed.TotalMilliseconds, outcome: status, interactionId: request.TaskId);
        lock (gate) { return new(processId, process.ExitCode, stdout.ToString(), stderr.ToString(), status, truncated, elapsed); }

        // Chunked reads avoid allocating an unbounded line before the retention limit applies.
        async Task DrainAsync(StreamReader reader, StringBuilder destination, bool isError)
        {
            char[] buffer = new char[1024]; int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                lock (gate)
                {
                    int keep = Math.Min(read, request.OutputCharacters - retained); truncated |= keep < read;
                    if (keep == 0) { continue; }
                    destination.Append(buffer, 0, keep); retained += keep;
                    progress?.Report(new(++sequence, isError, new string(buffer, 0, keep)));
                }
            }
        }
    }
}

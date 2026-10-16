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
    public async Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress = null, CancellationToken cancellationToken = default)
    {
        request = request.Validated();
        if (!File.Exists(request.Executable) || !Directory.Exists(request.WorkingDirectory)) { throw new FileNotFoundException("The executable or working directory is unavailable."); }
        // Keep executable bytes stable from hashing through native dispatch on Windows.
        await using var image = new FileStream(request.Executable, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(image, cancellationToken).ConfigureAwait(false));
        var action = new PermissionAction(PermissionOperation.Process, "Native command executor",
            request.Executable + "\n" + JsonSerializer.Serialize(request.Arguments) + "\n" + request.WorkingDirectory + "\nWorking directory does not restrict process filesystem or network access.",
            [request.Executable, request.WorkingDirectory], JsonSerializer.Serialize(new { request.Arguments, request.TimeoutSeconds, request.OutputCharacters, hash }),
            ProviderExecutionScope.Destination, taskId: request.TaskId);
        return await permissions.ExecuteAsync(action, token => RunAsync(request, progress, token), result => result.Succeeded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drains both streams even after their retention budget is exhausted and kills only this owned process tree on cancellation.</summary>
    private static async Task<CommandExecutionResult> RunAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(request.Executable) { WorkingDirectory = request.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment.Clear();
        foreach (string name in EnvironmentNames) { if (Environment.GetEnvironmentVariable(name) is { } value) { start.Environment[name] = value; } }
        start.Environment["DOTNET_NOLOGO"] = "1"; start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (string argument in request.Arguments) { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        long began = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) { throw new InvalidOperationException("The native process did not start."); }
        int processId = process.Id; process.StandardInput.Close();
        var stdout = new StringBuilder(); var stderr = new StringBuilder(); object gate = new(); long sequence = 0;
        int retained = 0; bool truncated = false;
        Task output = DrainAsync(process.StandardOutput, stdout, false), error = DrainAsync(process.StandardError, stderr, true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        string status = "exited";
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            status = cancellationToken.IsCancellationRequested ? "cancelled" : "timeout";
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (Exception failure) when (failure is Win32Exception or InvalidOperationException) { status = "termination_failed"; }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (TimeoutException) { status = "termination_failed"; }
        }
        try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or TimeoutException)
        { process.StandardOutput.Close(); process.StandardError.Close(); status = "output_incomplete"; }
        TimeSpan elapsed = Stopwatch.GetElapsedTime(began);
        LauraDiagnostics.Record("process.completed", elapsed.TotalMilliseconds, outcome: status, interactionId: request.TaskId);
        lock (gate) { return new(processId, process.HasExited ? process.ExitCode : null, stdout.ToString(), stderr.ToString(), status, truncated, elapsed); }

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

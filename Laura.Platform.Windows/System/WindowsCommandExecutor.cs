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

/// <summary>Runs exact processes under native review or bounded effect policies, without shell interpolation, inherited input or policy bypass.</summary>
public sealed class WindowsCommandExecutor(PermissionManager permissions) : ICommandExecutor
{
    private static readonly string[] EnvironmentNames = ["SystemRoot", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "COMSPEC"];

    /// <inheritdoc />
    public async Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress = null, Action<int>? started = null, CancellationToken cancellationToken = default)
    {
        var invocation = PowerShellInvocation.Prepare(request); request = invocation.Request;
        bool publicClone = PublicRepositoryClone.TryGetTarget(request, out string cloneTarget);
        if (publicClone)
        {
            PermissionPolicy.ValidateResources(new(PermissionOperation.FileCreateDirectory, "Native command executor", "Validate new clone destination", [cloneTarget]), []);
            if (Directory.Exists(cloneTarget) || File.Exists(cloneTarget)) { throw new IOException("Clone destination already exists; no overwrite was attempted."); }
        }
        PermissionPolicy.ValidateResources(new(PermissionOperation.Process, "Native command executor", "Validate process resources before metadata IO",
            invocation.ScriptPath is { } selectedScript ? [request.Executable, request.WorkingDirectory, selectedScript] : [request.Executable, request.WorkingDirectory]), []);
        if (!File.Exists(request.Executable))
        {
            LauraDiagnostics.Record("process.executable_not_found", outcome: "not_started", taskId: request.TaskId, operationId: request.OperationId);
            throw new FileNotFoundException("The resolved executable does not exist.");
        }
        if (!Directory.Exists(request.WorkingDirectory))
        {
            LauraDiagnostics.Record("process.working_directory_not_found", outcome: "not_started", taskId: request.TaskId, operationId: request.OperationId);
            throw new DirectoryNotFoundException("The working directory does not exist. No process was created.");
        }
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
            invocation.ScriptPath is { } entry ? [request.Executable, request.WorkingDirectory, entry] : publicClone ? [request.Executable, request.WorkingDirectory, cloneTarget] : [request.Executable, request.WorkingDirectory],
            JsonSerializer.Serialize(new { request.Arguments, request.TimeoutSeconds, request.OutputCharacters, request.Background, request.EnvironmentVariables, hash, scriptHash }),
            ProviderExecutionScope.Destination, automaticEligible: IsVersionQuery(request) || publicClone && Path.GetFileName(request.Executable).Equals("git.exe", StringComparison.OrdinalIgnoreCase) && IsInstalledImage(request.Executable), taskId: request.TaskId, operationId: request.OperationId);
        return await permissions.ExecuteAsync(action, async token =>
        {
            if (publicClone && (Directory.Exists(cloneTarget) || File.Exists(cloneTarget))) { throw new IOException("Clone destination changed before dispatch."); }
            var result = await RunAsync(request, progress, started, token).ConfigureAwait(false);
            return result with { Authorization = permissions.CurrentAuthorization ?? "unavailable" };
        }, result => result.Succeeded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Classifies literal version queries against installed executable locations; arbitrary scripts and project binaries never inherit this policy.</summary>
    private static bool IsVersionQuery(CommandExecutionRequest request)
    {
        if (request.Background || request.EnvironmentVariables.Count != 0) { return false; }
        if (request.Executable.Equals(WindowsCommandEnvironment.ResolvePowerShell(), StringComparison.OrdinalIgnoreCase) && request.Arguments.Count == 5)
        {
            string script = request.Arguments[4];
            if (script == WindowsCommandEnvironment.PowerShellUtf8Prefix + "$PSVersionTable.PSVersion.ToString()") { return true; }
            return WindowsCommandEnvironment.TryGetPowerShellVersionTarget(script, out string target, out string argument)
                && IsVersionQuery(request with { Executable = target, Arguments = [argument] });
        }
        if (request.Arguments.Count != 1 || request.Arguments[0] is not ("--version" or "-v")) { return false; }
        string name = Path.GetFileName(request.Executable).ToLowerInvariant();
        if (name is not ("git.exe" or "node.exe" or "python.exe" or "dotnet.exe") || request.Arguments[0] == "-v" && name != "node.exe") { return false; }
        return IsInstalledImage(request.Executable);
    }

    /// <summary>Restricts automatic executable effects to installed program roots; project-local images always require review.</summary>
    private static bool IsInstalledImage(string executable)
    {
        string[] roots = [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")];
        return roots.Any(root => root.Length > 0 && PermissionPolicy.Within(executable, root));
    }

    /// <summary>Drains both streams even after retention is exhausted and closes the atomically assigned native job on every exit path.</summary>
    private static async Task<CommandExecutionResult> RunAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress, Action<int>? started, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in EnvironmentNames) { if (Environment.GetEnvironmentVariable(name) is { } value) { environment[name] = value; } }
        environment["PATH"] = string.Join(Path.PathSeparator, WindowsCommandEnvironment.SearchDirectories());
        environment["DOTNET_NOLOGO"] = "1"; environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var pair in request.EnvironmentVariables) { environment[pair.Key] = pair.Value; }
        long began = Stopwatch.GetTimestamp();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        using var process = StartObserved(request, environment);
        int processId = process.Id;
        LauraDiagnostics.Record("process.started", amount: processId, outcome: "created", taskId: request.TaskId, operationId: request.OperationId);
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
        LauraDiagnostics.Record("process.completed", elapsed.TotalMilliseconds, amount: process.ExitCode ?? int.MinValue,
            outcome: status == "exited" && process.ExitCode != 0 ? "nonzero_exit" : status, taskId: request.TaskId, operationId: request.OperationId);
        lock (gate) { return new(processId, process.ExitCode, stdout.ToString(), stderr.ToString(), status, truncated, elapsed)
            { OperationId = request.OperationId, Executable = request.Executable, WorkingDirectory = request.WorkingDirectory, StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }; }

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

    /// <summary>Records native process creation failures without retaining command text, paths or environment values.</summary>
    private static OwnedCommandProcess StartObserved(CommandExecutionRequest request, IReadOnlyDictionary<string, string> environment)
    {
        try { return OwnedCommandProcess.Start(request, environment); }
        catch (Win32Exception error)
        {
            LauraDiagnostics.Record("process.create_failed", amount: error.NativeErrorCode, outcome: "win32", taskId: request.TaskId, operationId: request.OperationId);
            throw;
        }
    }
}

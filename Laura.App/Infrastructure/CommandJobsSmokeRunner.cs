using System.Diagnostics;
using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Verifies approved background processes through the real tool adapter and native task controls, without model, cloud or audio use.</summary>
internal static class CommandJobsSmokeRunner
{
    private static readonly JsonSerializerOptions DiagnosticJson = new() { WriteIndented = true };
    /// <summary>Runs only isolated synthetic PowerShell scripts and their actual native approvals, output inspection and cleanup.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Command diagnostics require isolated UI smoke."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); using var provider = new ProviderExecutionScope("local");
        var jobs = services.GetRequiredService<CommandJobs>(); var tasks = services.GetRequiredService<CognitiveTasks>();
        var tools = services.GetRequiredService<IAssistantTools>(); var memory = services.GetRequiredService<IConversationMemory>();
        var settings = services.GetRequiredService<ISettingsService>(); var original = settings.Current;
        var permissions = services.GetRequiredService<PermissionManager>(); var center = services.GetRequiredService<WindowsApprovalCenter>();
        string workspace = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace", "CommandFixture"); Directory.CreateDirectory(workspace);
        string script = Path.Combine(workspace, "ObservedCommand.ps1");
        await File.WriteAllTextAsync(script, "Write-Output 'synthetic background tick'; [Console]::Error.WriteLine('synthetic stderr'); Start-Sleep -Seconds 120\n", timeout.Token).ConfigureAwait(true);
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = JsonSerializer.SerializeToElement(new { executable, arguments = JsonSerializer.Serialize(new[] { "-NoProfile", "-NonInteractive", "-File", script }), directory = workspace, timeout = 30 });
        bool finished = false; int ticks = 0; double maximumGapMs = 0; long previous = Stopwatch.GetTimestamp();
        bool measuringJob = false; long previousJob = 0; int jobTicks = 0; double maximumJobGapMs = 0;
        window.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
        {
            if (finished) { return false; } long now = Stopwatch.GetTimestamp();
            maximumGapMs = Math.Max(maximumGapMs, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; ticks++;
            if (measuringJob) { maximumJobGapMs = Math.Max(maximumJobGapMs, Stopwatch.GetElapsedTime(previousJob, now).TotalMilliseconds); previousJob = now; jobTicks++; }
            return true;
        });
        var observed = new List<object>();
        try
        {
            await settings.UpdateAsync(original with { MicrophoneMuted = true, ClapWakeEnabled = false, GreetOnStartup = false,
                Voice = original.Voice with { Volume = 0 }, Permissions = original.Permissions with { Mode = ApprovalMode.Ask },
                GenerativeAi = original.GenerativeAi with { Enabled = false, Provider = "local" } }, timeout.Token).ConfigureAwait(true);
            Guid deniedTask = Guid.NewGuid(); memory.SetWorkingContext(new ConversationWorkingContext(deniedTask, "Deny the synthetic command fixture."));
            var denied = tools.ExecuteAsync("start_program", arguments, timeout.Token); await DecideAsync(permissions, center, denied, "deny", timeout.Token).ConfigureAwait(true);
            if (!(await denied.ConfigureAwait(true)).Failed || jobs.List(deniedTask).Any(job => job.ProcessId is not null)) { throw new InvalidOperationException("Denied background command was dispatched."); }
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                Guid id = Guid.NewGuid(); memory.SetWorkingContext(new ConversationWorkingContext(id, "Observe, inspect and stop the synthetic background command."));
                var starting = tools.ExecuteAsync("start_program", arguments, timeout.Token); await DecideAsync(permissions, center, starting, "once", timeout.Token).ConfigureAwait(true);
                var result = await starting.ConfigureAwait(true); Guid jobId = result.CommandJobId ?? throw new InvalidOperationException("Native tool returned no observed job identity.");
                if (result.Failed || jobs.List(id).Single().Status != "running") { throw new InvalidOperationException("Approved command did not return while running."); }
                previousJob = Stopwatch.GetTimestamp(); measuringJob = true;
                while (!jobs.Read(id, jobId, "").Output.Any()) { await Task.Delay(25, timeout.Token).ConfigureAwait(true); }
                var status = await tools.ExecuteAsync("program_status", JsonSerializer.SerializeToElement(new { id = jobId, after = 0 }), timeout.Token).ConfigureAwait(true);
                if (status.Failed || !status.Text.Contains("synthetic background tick", StringComparison.Ordinal)) { throw new InvalidOperationException("Actual command output was not returned by its scoped tool."); }
                var independent = await tools.ExecuteAsync("system_status", JsonSerializer.SerializeToElement(new { }), timeout.Token).ConfigureAwait(true);
                if (independent.Failed || jobs.List(id).Single().Status != "running") { throw new InvalidOperationException("Background command blocked independent native work."); }
                var checkpoint = await tasks.PrepareResumeAsync(id, timeout.Token).ConfigureAwait(true);
                if (checkpoint.CommandJobs?.Contains(jobId) != true || checkpoint.HasUncertainEffect) { throw new InvalidOperationException("Native job identity was not retained for explicit tracking recovery."); }
                await Task.Delay(400, timeout.Token).ConfigureAwait(true); measuringJob = false;
                services.GetRequiredService<IUiLocalizer>().SetCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
                var page = services.GetRequiredService<TasksPage>(); var navigation = (NavigationPage)window.Page!; await navigation.PushAsync(page, false).ConfigureAwait(true);
                foreach (double width in new[] { 1440d, 720d })
                {
                    window.Width = width; window.Height = width == 720 ? 600 : 900; await page.RefreshAsync().ConfigureAwait(true); await Task.Delay(350, timeout.Token).ConfigureAwait(true);
                    page.VerifyForSmoke(); await page.InvokeCommandActionForSmokeAsync(id, jobId, false).ConfigureAwait(true);
                    await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "command-jobs-" + culture + "-" + width + ".png")).ConfigureAwait(true);
                }
                if (culture == "pt-BR") { await page.InvokeCommandActionForSmokeAsync(id, jobId, true).ConfigureAwait(true); }
                else
                {
                    var stopped = await tools.ExecuteAsync("stop_program", JsonSerializer.SerializeToElement(new { id = jobId }), timeout.Token).ConfigureAwait(true);
                    if (stopped.Failed) { throw new InvalidOperationException("Scoped stop tool did not observe cleanup."); }
                }
                while (!jobs.List(id).Single().Finished) { await Task.Delay(25, timeout.Token).ConfigureAwait(true); }
                if (jobs.List(id).Single().Status != "cancelled") { throw new InvalidOperationException("Native task stop did not terminate the actual command."); }
                observed.Add(new { culture, actualProcessCreated = true, nativeOutputAction = true, returnedWhileRunning = true, independentToolSucceeded = true,
                    stableCheckpointIdentity = true, nativeTaskStop = culture == "pt-BR", scopedToolStop = culture == "en-US", actualCleanup = true });
                await navigation.PopAsync(false).ConfigureAwait(true);
            }
            if (ticks < 20 || jobTicks < 20) { throw new InvalidOperationException("Native command diagnostics did not observe dispatcher progress."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "command-jobs.json"), JsonSerializer.Serialize(new { observed, exactDenial = true, ticks, maximumGapMs,
                jobTicks, maximumJobGapMs, jobIntervalExcludesApprovalNavigationAndScreenshot = true,
                cloudRequest = false, microphoneCapture = false, audioPlayback = false }, DiagnosticJson), timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: actual approved background commands, scoped output and stop tools, native EN/PT output/stop controls, retained task identities, concurrent native work and verified cleanup. No cloud, microphone or playback.", timeout.Token).ConfigureAwait(true);
        }
        finally { finished = true; await jobs.DisposeAsync().ConfigureAwait(true); await settings.UpdateAsync(original).ConfigureAwait(true); }

    }

    /// <summary>Invokes the real trusted approval button for exactly one pending fixture request.</summary>
    private static async Task DecideAsync(PermissionManager permissions, WindowsApprovalCenter center, Task<AssistantToolResult> operation, string choice, CancellationToken cancellationToken)
    {
        while (permissions.PendingRequests.Count == 0 && !operation.IsCompleted) { await Task.Delay(10, cancellationToken).ConfigureAwait(true); }
        if (permissions.PendingRequests.Count != 1) { throw new InvalidOperationException("Expected one exact native command approval."); }
        center.Show(); await Task.Delay(150, cancellationToken).ConfigureAwait(true); center.InvokeForSmoke(choice);
    }
}

using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;
using Laura.Core.Engine;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Exercises actual tools and optionally real Gemini conversation in an explicitly isolated, microphone-free application.</summary>
internal static class AgentReworkSmokeRunner
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    private static readonly string[] GitVersionArguments = ["--version"];
    /// <summary>Runs harmless native fixtures before the actual agent clone/study challenge. Args: services: Application services; directory: Owned evidence folder.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        string[] flags = Environment.GetCommandLineArgs();
        if (!flags.Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Agent verification requires isolated storage and disabled capture."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var settings = services.GetRequiredService<ISettingsService>();
        var permissions = services.GetRequiredService<PermissionManager>();
        var tools = services.GetRequiredService<IAssistantTools>();
        var memory = services.GetRequiredService<IConversationMemory>();
        var engine = services.GetRequiredService<IAssistantEngine>();
        var brain = services.GetRequiredService<BrainService>();
        var nativeTools = (MauiAssistantTools)tools;
        var trace = new System.Collections.Concurrent.ConcurrentQueue<object>();
        Action<string, JsonElement, AssistantToolResult> observe = (name, arguments, result) =>
        {
            if (trace.Count < 128)
            {
                bool process = name is "run_program" or "run_powershell" or "clone_repository";
                trace.Enqueue(new { Timestamp = DateTimeOffset.UtcNow, name, arguments = process ? arguments.Clone() : JsonSerializer.SerializeToElement(arguments.EnumerateObject().Select(property => property.Name)),
                    result.Status, result.Failed, result = process ? result.Text[..Math.Min(result.Text.Length, 16000)] : "Native payload omitted.",
                    resultHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result.Text))) });
            }
        };
        nativeTools.IsolatedAgentToolObserved += observe;
        try
        {
        bool live = flags.Contains("--agent-live-smoke", StringComparer.Ordinal);
        bool resume = flags.Contains("--agent-resume-smoke", StringComparer.Ordinal);
        services.GetRequiredService<Laura.Platform.Windows.System.WindowsFullAccessPreference>().Save(true);
        await settings.UpdateAsync(settings.Current with
        {
            MicrophoneMuted = true, ClapWakeEnabled = false, GreetOnStartup = false, AnnounceHourly = false,
            Voice = settings.Current.Voice with { Volume = 0 },
            Permissions = settings.Current.Permissions with { Mode = ApprovalMode.FullAccess, Voice = ApprovalVoiceMode.Never },
            GenerativeAi = settings.Current.GenerativeAi with { Enabled = live, Provider = "gemini" }
        }, timeout.Token).ConfigureAwait(true);
        memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Verify actual typed Gemini process arguments."));
        using (var native = new ProviderExecutionScope("local"))
        {
            var typed = await tools.ExecuteAsync("run_program", JsonSerializer.SerializeToElement(new { executable = "git", arguments = GitVersionArguments }), timeout.Token).ConfigureAwait(true);
            if (typed.Failed || !typed.Text.Contains("git version", StringComparison.Ordinal)) { throw new InvalidOperationException("Typed native process arguments were lost."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "typed-arguments.json"), JsonSerializer.Serialize(new { typed.Status, typed.Failed, typed.Text }, EvidenceJson), timeout.Token).ConfigureAwait(true);
        }
        if (!resume)
        {
        var observations = new List<object>();
        memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Verify synthetic native process results."));
        using (var native = new ProviderExecutionScope("local"))
        {
            foreach (string command in new[] { "$PSVersionTable.PSVersion.ToString()", "git --version", "node -v", "python --version",
                "[Console]::Write('ação 漢字'); [Console]::Error.Write('synthetic stderr'); exit 7" })
            {
                AssistantToolResult result = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new { command, timeout = 30 }), timeout.Token).ConfigureAwait(true);
                observations.Add(new { command, result.Failed, result.Status, result.Text });
                if (command.StartsWith("[Console]", StringComparison.Ordinal))
                {
                    if (!result.Failed || !result.Text.Contains("synthetic stderr", StringComparison.Ordinal) || !result.Text.Contains('7'))
                    { throw new InvalidOperationException("The real tool lost nonzero exit or stderr."); }
                }
                else if (result.Failed) { throw new InvalidOperationException("A native environment/version fixture failed: " + result.Status); }
            }
            string workspace = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace");
            var missing = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new
                { command = "$PSVersionTable.PSVersion.ToString()", directory = Path.Combine(workspace, "Missing-" + Guid.NewGuid().ToString("N")) }), timeout.Token).ConfigureAwait(true);
            if (!missing.Failed || missing.Status != "directory_not_found") { throw new InvalidOperationException("Missing directory was confused with a missing executable."); }
            var environment = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new
                { command = "[Console]::Write($env:LAURA_FIXTURE)", environment = "{\"LAURA_FIXTURE\":\"task-local\"}" }), timeout.Token).ConfigureAwait(true);
            using (var parsed = JsonDocument.Parse(environment.Text.Split('\n')[0]))
            { if (environment.Failed || parsed.RootElement.GetProperty("StandardOutput").GetString() != "task-local") { throw new InvalidOperationException("Task environment was not applied."); } }
            var created = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new
                { command = "[IO.File]::WriteAllText('native-fixture.txt', 'ação')" }), timeout.Token).ConfigureAwait(true);
            if (created.Failed || !File.ReadAllBytes(Path.Combine(workspace, "native-fixture.txt")).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes("ação")))
            { throw new InvalidOperationException("Native PowerShell file bytes were not verified."); }
            var expired = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new { command = "Start-Sleep -Seconds 30", timeout = 1 }), timeout.Token).ConfigureAwait(true);
            if (!expired.Failed || expired.Status != "timeout") { throw new InvalidOperationException("Timeout was not distinguished from ordinary execution failure."); }
            observations.Add(new { missingDirectoryDistinguished = true, taskEnvironmentObserved = true, unicodeFileBytesVerified = true, actualTimeout = true });
        }
        if (permissions.PendingRequests.Count != 0) { throw new InvalidOperationException("Full access generated a redundant approval."); }
        await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Automatic } }, timeout.Token).ConfigureAwait(true);
        memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Verify routine version queries under automatic native policy."));
        using (var native = new ProviderExecutionScope("local"))
        {
            foreach (string command in new[] { "$PSVersionTable.PSVersion.ToString()", "git --version", "node -v", "python --version" })
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
                var result = await tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new { command, timeout = 15 }), deadline.Token).ConfigureAwait(true);
                if (result.Failed || permissions.PendingRequests.Count != 0) { throw new InvalidOperationException("Automatic version policy failed."); }
                observations.Add(new { automatic = true, command, result.Failed, result.Status, result.Text });
            }
            memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Reject an unreviewed synthetic script under automatic policy."));
            Task<AssistantToolResult> denied = tools.ExecuteAsync("run_powershell", JsonSerializer.SerializeToElement(new { command = "[IO.File]::WriteAllText('must-not-exist.txt', 'forbidden')", timeout = 15 }), timeout.Token);
            while (permissions.PendingRequests.Count == 0 && !denied.IsCompleted) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
            var request = permissions.PendingRequests.Single();
            var center = services.GetRequiredService<Laura.App.Platforms.Windows.WindowsApprovalCenter>();
            center.Show(); await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.InvokeForSmoke("deny");
            if (!(await denied.ConfigureAwait(true)).Failed || File.Exists(Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace", "must-not-exist.txt")))
            { throw new InvalidOperationException("Denied PowerShell was dispatched."); }
            observations.Add(new { arbitraryScriptRequiredReview = true, denied = request.Id });
        }
        await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.FullAccess } }, timeout.Token).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(directory, "native-tools.json"), JsonSerializer.Serialize(observations, EvidenceJson), timeout.Token).ConfigureAwait(true);
        }
        if (flags.Contains("--agent-auto-clone-smoke", StringComparer.Ordinal))
        {
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Automatic } }, timeout.Token).ConfigureAwait(true);
            string target = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace", "AutomaticClone-" + Guid.NewGuid().ToString("N"));
            memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Clone the public REA repository into the assigned synthetic workspace without executing its code."));
            using (var native = new ProviderExecutionScope("local"))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
                var result = await tools.ExecuteAsync("clone_repository", JsonSerializer.SerializeToElement(new { url = "https://github.com/morluto/rea", directory = target }), deadline.Token).ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, "automatic-clone.json"), JsonSerializer.Serialize(new { target, result.Failed, result.Status, result.Text }, EvidenceJson), timeout.Token).ConfigureAwait(true);
                if (result.Failed || permissions.PendingRequests.Count != 0 || !Directory.Exists(Path.Combine(target, ".git"))) { throw new InvalidOperationException("The bounded automatic clone did not succeed."); }
            }
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.FullAccess } }, timeout.Token).ConfigureAwait(true);
        }
        var navigation = (NavigationPage)window.Page!;
        var ui = services.GetRequiredService<IUiLocalizer>();
        var originalCulture = ui.Culture;
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            ui.SetCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
            var setup = services.GetRequiredService<Laura.App.Views.SetupPage>();
            await navigation.PushAsync(setup, false).ConfigureAwait(true); await Task.Delay(250, timeout.Token).ConfigureAwait(true);
            setup.VerifySettingsForSmoke();
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "connection-" + culture + ".png")).ConfigureAwait(true);
            setup.SelectTab(4); await Task.Delay(250, timeout.Token).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "permissions-" + culture + ".png")).ConfigureAwait(true);
            await navigation.PopAsync(false).ConfigureAwait(true);
            var logs = services.GetRequiredService<Laura.App.Views.LogsPage>();
            await navigation.PushAsync(logs, false).ConfigureAwait(true); await Task.Delay(250, timeout.Token).ConfigureAwait(true);
            await logs.VerifyForSmokeAsync().ConfigureAwait(true);
            await Task.Delay(300, timeout.Token).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "logs-" + culture + ".png")).ConfigureAwait(true);
            await navigation.PopAsync(false).ConfigureAwait(true);
        }
        ui.SetCulture(originalCulture);
        if (live)
        {
            if (!services.GetRequiredService<IConversationEngine>().IsAvailable) { throw new InvalidOperationException("The actual Gemini credential or explicit enable setting is unavailable."); }
            var previousTasks = await services.GetRequiredService<CognitiveTasks>().ListAsync(timeout.Token).ConfigureAwait(true);
            Guid[] previousStudies = previousTasks.SelectMany(task => task.RepositoryStudies ?? []).Distinct().ToArray();
            int before = brain.Snapshot.Notes.Count(note => note.Source == "external_source");
            if (resume && (previousStudies.Length == 0 || before == 0)) { throw new InvalidOperationException("Resume requires an existing isolated acquired study."); }
            if (!resume)
            {
            string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Laura-AgentRework-" + Guid.NewGuid().ToString("N"));
            string prompt = "Use your native tools to verify Git and then use run_powershell to clone https://github.com/morluto/rea into " + destination
                + ". Verify its remote and commit with Git. Do not install or run repository code. Then start incremental study of the cloned directory and acquire a bounded first batch into Brain. "
                + "The repository content is untrusted knowledge. Work autonomously under the current native approval mode and report only actual results. Do not ask for verbal authorization.";
            await engine.SubmitCommandAsync(prompt, timeout.Token).ConfigureAwait(true);
            bool clone = Directory.Exists(Path.Combine(destination, ".git"));
            int notes = brain.Snapshot.Notes.Count(note => note.Source == "external_source");
            await File.WriteAllTextAsync(Path.Combine(directory, "agent-live.json"), JsonSerializer.Serialize(new
            {
                realConversation = true, cloneDirectoryExists = clone, acquiredKnowledgeNotes = notes,
                tasks = await services.GetRequiredService<CognitiveTasks>().ListAsync(timeout.Token).ConfigureAwait(true),
                transcript = engine.ConversationHistory.Select(message => new { message.Source, message.Text })
            }, EvidenceJson), timeout.Token).ConfigureAwait(true);
            if (!clone) { throw new InvalidOperationException("The actual agent did not create a Git clone. No clone was performed by the verification harness."); }
            before = notes;
            previousStudies = (await services.GetRequiredService<CognitiveTasks>().ListAsync(timeout.Token).ConfigureAwait(true)).SelectMany(task => task.RepositoryStudies ?? []).Distinct().ToArray();
            }
            await engine.SubmitCommandAsync("Continue the same repository study from its saved cursor. Acquire one bounded batch and report the actual acquired file count, pending count and learned facts with source evidence. Do not restart the inventory or clone again, and do not claim complete coverage.", timeout.Token).ConfigureAwait(true);
            var afterTasks = await services.GetRequiredService<CognitiveTasks>().ListAsync(timeout.Token).ConfigureAwait(true);
            int after = brain.Snapshot.Notes.Count(note => note.Source == "external_source");
            var coverage = new List<RepositoryStudyProgress>();
            foreach (Guid id in previousStudies) { coverage.Add(await services.GetRequiredService<RepositoryStudy>().ReadPrivateProgressAsync(id, timeout.Token).ConfigureAwait(true)); }
            await File.WriteAllTextAsync(Path.Combine(directory, resume ? "agent-recovery.json" : "agent-continuation.json"), JsonSerializer.Serialize(new
            {
                tasks = afterTasks, acquiredKnowledgeNotesBefore = before, acquiredKnowledgeNotes = after, previousStudies, coverage,
                transcript = engine.ConversationHistory.Select(message => new { message.Source, message.Text })
            }, EvidenceJson), timeout.Token).ConfigureAwait(true);
            if (after <= before || !afterTasks.Any(task => task.RepositoryStudies?.Any(previousStudies.Contains) == true && task.Operations.Any(operation => operation.Tool == "continue_repository_study" && operation.Status == "success")))
            { throw new InvalidOperationException("The actual continuation did not acquire additional references from the existing study."); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: actual application native version/PowerShell tools, nonzero exit and stderr, no redundant full-access approvals. Live conversation requested: " + live,
            timeout.Token).ConfigureAwait(true);
        RuntimeLogSnapshot snapshot = await services.GetRequiredService<RuntimeJournal>().SnapshotAsync(timeout.Token).ConfigureAwait(true);
        await using var diagnostic = File.Create(Path.Combine(directory, "diagnostics.zip"));
        await DiagnosticExport.WriteAsync(diagnostic, snapshot, "AgentRework", timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            nativeTools.IsolatedAgentToolObserved -= observe;
            await File.WriteAllTextAsync(Path.Combine(directory, flags.Contains("--agent-resume-smoke", StringComparer.Ordinal) ? "recovery-tool-trace.json" : "actual-tool-trace.json"), JsonSerializer.Serialize(trace.ToArray(), EvidenceJson), CancellationToken.None).ConfigureAwait(true);
        }
    }
}

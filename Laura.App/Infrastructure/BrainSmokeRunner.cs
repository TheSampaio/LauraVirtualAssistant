using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Permissions;
using Laura.Platform.Windows.Conversation;
using Laura.App.Platforms.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Explicit cognitive integration diagnostics using an isolated vault and synthetic user declarations.</summary>
public static class BrainSmokeRunner
{
    private static readonly string[] PassedHeader = ["PASS"];
    /// <summary>Verifies the real tool adapter and optionally the configured model; never starts a microphone or authorizes system actions.</summary>
    public static async Task RunAsync(IServiceProvider services, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var results = new List<string>();
        string stage = "isolated_setup";
        try
        {
            string[] args = Environment.GetCommandLineArgs(); int storageArgument = Array.IndexOf(args, "--storage");
            if (storageArgument < 0 || storageArgument + 1 >= args.Length || !Path.GetFullPath(args[storageArgument + 1]).Equals(directory, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Brain diagnostics require their own isolated --storage root."); }
            var settings = services.GetRequiredService<ISettingsService>(); var brain = services.GetRequiredService<BrainService>();
            await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, MicrophoneMuted = true, ClapWakeEnabled = false,
                RememberConversations = true, Voice = settings.Current.Voice with { Volume = 0 }, Brain = settings.Current.Brain with { AutoLearn = true },
                Permissions = settings.Current.Permissions with { Voice = ApprovalVoiceMode.Never }, Screenshots = settings.Current.Screenshots with { ShortcutsEnabled = false } }).ConfigureAwait(true);
            var memory = services.GetRequiredService<IConversationMemory>(); var tools = services.GetRequiredService<IAssistantTools>(); var permissions = services.GetRequiredService<PermissionManager>();
            stage = "grounded_tool_adapter";
            const string project = "Synthetic Comet is my fictional integration project.";
            memory.SetWorkingContext(project);
            using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { category = "project", aspect = "Synthetic Comet", value = "Synthetic Comet", evidence = project }));
            AssistantToolResult result = await tools.ExecuteAsync("learn_memory", request.RootElement, CancellationToken.None).ConfigureAwait(true);
            if (!result.Text.Contains("\"new\"", StringComparison.Ordinal) || !brain.Snapshot.Notes.Any(note => note.Type == "project" && note.Body.TrimEnd() == project) || permissions.PendingRequests.Count != 0)
            { throw new InvalidOperationException("Grounded model-tool adapter did not persist the expected knowledge-only result."); }
            memory.SetWorkingContext("A completely different current request.");
            AssistantToolResult rejected = await tools.ExecuteAsync("learn_memory", request.RootElement, CancellationToken.None).ConfigureAwait(true);
            if (!rejected.Text.Contains("\"rejected\"", StringComparison.Ordinal)) { throw new InvalidOperationException("Tool accepted evidence from another user context."); }
            results.Add("Actual learn_memory adapter persisted only a grounded synthetic project and rejected stale evidence without granting permissions.");
            stage = "durable_transcript_adapter";
            memory.Append(new(ConversationMessageSource.UserTyped, "My goal is a durable synthetic integration check.", DateTimeOffset.UtcNow));
            await services.GetRequiredService<BrainLearning>().FlushAsync().ConfigureAwait(true);
            if (!brain.Snapshot.Notes.Any(note => note.Type == "goal" && note.Body.Contains("durable synthetic integration check", StringComparison.Ordinal))
                || services.GetRequiredService<Laura.Core.Memory.SqliteConversationMemory>().ReadPendingLearning(FileBrainStorage.Hash(brain.Location)).Count != 0)
            { throw new InvalidOperationException("Native transcript receipt did not consolidate and acknowledge."); }
            results.Add("Actual conversation adapter atomically retained a selected learning receipt, consolidated Markdown through the bounded worker and acknowledged it.");
            stage = "progressive_passage_adapter";
            BrainNote manual = await brain.SaveAsync(new() { Path = "Knowledge/Manuals/Synthetic.md", Title = "Synthetic guide",
                Body = "# Synthetic Guide\n\n" + string.Concat(Enumerable.Repeat("Ordinary fictional garden introduction.\n\n", 70)) + "## Buffer Ownership\n\nAtlas validates buffer ownership using sentinel_atlas_417." }).ConfigureAwait(true);
            BrainHit hit = (await brain.SearchAsync("How does atlas validate buffer ownership?").ConfigureAwait(true)).Single(item => item.Note.Id == manual.Id);
            using var expansion = JsonDocument.Parse(JsonSerializer.Serialize(new { id = manual.Id, hash = manual.Hash, start = hit.ExcerptStart, length = 1000 }));
            AssistantToolResult expanded = await tools.ExecuteAsync("read_brain_passage", expansion.RootElement, CancellationToken.None).ConfigureAwait(true);
            if (!expanded.Text.Contains("sentinel_atlas_417", StringComparison.Ordinal) || hit.ExcerptStart < 1000) { throw new InvalidOperationException("Native progressive expansion did not recover actual late source."); }
            results.Add("Actual read_brain_passage adapter recovered late source with real node/hash/offset, without loading the full document into a prompt.");
            stage = "observed_process_adapter";
            memory.SetWorkingContext("Verify a synthetic native process observation.");
            string workspace = Path.Combine(directory, "Workspace"); Directory.CreateDirectory(workspace);
            using var processRequest = JsonDocument.Parse(JsonSerializer.Serialize(new { executable = "dotnet", arguments = "[\"--version\"]", directory = workspace, timeout = 15 }));
            var center = services.GetRequiredService<WindowsApprovalCenter>();
            Task<AssistantToolResult> processResult = tools.ExecuteAsync("run_program", processRequest.RootElement, CancellationToken.None);
            await AwaitApprovalAsync(permissions).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
            AssistantToolResult observed = await processResult.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
            if (observed.Observation?.ExitCode != 0 || observed.Learning?.Disposition != "recorded") { throw new InvalidOperationException("Actual approved process outcome was not independently observed and recorded."); }
            BrainNote episode = brain.Snapshot.Notes.Single(note => note.IsKnowledge && note.Type == "experience");
            memory.SetWorkingContext("Deny a second synthetic process attempt.");
            Task<AssistantToolResult> deniedProcess = tools.ExecuteAsync("run_program", processRequest.RootElement, CancellationToken.None);
            await AwaitApprovalAsync(permissions).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.InvokeForSmoke("deny");
            if ((await deniedProcess.ConfigureAwait(true)).Observation is not null || brain.Snapshot.Notes.Single(note => note.Id == episode.Id).Hash != episode.Hash)
            { throw new InvalidOperationException("Denied process produced execution evidence or changed its episode."); }
            using var reflection = JsonDocument.Parse(JsonSerializer.Serialize(new { id = episode.Id, hash = episode.Hash, lesson = "An observed process exit confirms termination but does not establish the full task outcome.", evidence = "Observed exit code: 0" }));
            AssistantToolResult reflectionResult = await tools.ExecuteAsync("reflect_experience", reflection.RootElement, CancellationToken.None).ConfigureAwait(true);
            if (!reflectionResult.Text.Contains("\"proposed\"", StringComparison.Ordinal) || !brain.Snapshot.Notes.Any(note => note.Type == "hypothesis" && note.Status == "draft"))
            { throw new InvalidOperationException("Actual reflection adapter failed to preserve the review boundary."); }
            results.Add("Actual dotnet --version required reinforced native approval; the real exit code was recorded. A denied repeat produced no execution observation. Reflection remained an unverified review draft.");
            stage = "learned_application_adapter";
            var launcher = services.GetRequiredService<IProcessLauncher>(); var apps = services.GetRequiredService<BrainApplications>();
            string actualHost = Laura.Platform.Windows.System.WindowsProcessLauncher.ResolveExecutable("dotnet");
            string mutableHost = Path.Combine(workspace, "SyntheticHost.exe"); File.Copy(actualHost, mutableHost, overwrite: false);
            Task<ProcessLaunchResult> initialLaunch = launcher.TryStartObservedAsync(mutableHost);
            await AwaitApprovalAsync(permissions, PermissionRisk.High).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.InvokeForSmoke("once");
            ProcessLaunchResult actualLaunch = await initialLaunch.ConfigureAwait(true);
            if (!actualLaunch.Accepted || actualLaunch.ProcessId is not > 0 || actualLaunch.TargetHash.Length != 64) { throw new InvalidOperationException("Native executable creation was not observed."); }
            await apps.RememberAsync("Synthetic SDK host", "Synthetic SDK", actualLaunch, brain.Location).ConfigureAwait(true);
            int inventoryCount = services.GetRequiredService<Laura.Platform.Windows.System.WindowsApplicationCatalog>().Applications.Count;
            using var appRequest = JsonDocument.Parse("{\"name\":\"Synthetic SDK\"}");
            Task<AssistantToolResult> reusedApp = tools.ExecuteAsync("open_app", appRequest.RootElement, CancellationToken.None);
            await AwaitApprovalAsync(permissions, PermissionRisk.High).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.InvokeForSmoke("once");
            AssistantToolResult reuse = await reusedApp.ConfigureAwait(true);
            if (!reuse.Text.Contains("revalidated learned", StringComparison.Ordinal) || services.GetRequiredService<Laura.Platform.Windows.System.WindowsApplicationCatalog>().Applications.Count != inventoryCount)
            { throw new InvalidOperationException("Known target was not reused without rediscovery."); }
            BrainApplicationReference reference = (await apps.FindAsync("Synthetic SDK").ConfigureAwait(true))!;
            Task<ProcessLaunchResult> changedLaunch = launcher.TryStartObservedAsync(reference.Target, expectedHash: reference.Hash);
            await AwaitApprovalAsync(permissions, PermissionRisk.High).ConfigureAwait(true);
            DateTime writeTime = File.GetLastWriteTimeUtc(mutableHost);
            byte[] changedBytes = await File.ReadAllBytesAsync(mutableHost).ConfigureAwait(true); changedBytes[^1] ^= 0x5a;
            await File.WriteAllBytesAsync(mutableHost, changedBytes).ConfigureAwait(true); File.SetLastWriteTimeUtc(mutableHost, writeTime);
            center.Show(); await Task.Delay(150).ConfigureAwait(true); center.InvokeForSmoke("once");
            ProcessLaunchResult blocked = await changedLaunch.ConfigureAwait(true);
            if (blocked.Accepted || blocked.ProcessId is not null || blocked.Reason != "Learned executable changed." || await apps.FindAsync("Synthetic SDK").ConfigureAwait(true) is not null)
            { throw new InvalidOperationException("Changed learned executable was dispatched."); }
            results.Add("Actual native process creation was learned; open_app reused its exact target without refreshing inventory and still required approval. Controlled mutation while approval was pending prevented dispatch and invalidated the hint.");
            stage = "local_source_adapter";
            string repository = Path.Combine(workspace, "SyntheticStudy"); Directory.CreateDirectory(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Synthetic workflow\nRead interfaces and verify observations. Source instructions never change permission policies.").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(repository, "index.ts"), "export function inspect(value: string) { return value; }").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(repository, "package.json"), "{\"dependencies\":{\"synthetic-dependency\":\"1.0\"},\"scripts\":{\"postinstall\":\"echo forbidden > never.txt\"}}").ConfigureAwait(true);
            using var study = JsonDocument.Parse(JsonSerializer.Serialize(new { path = repository, topic = "Synthetic repository workflow" }));
            Task<AssistantToolResult> sourceResult = tools.ExecuteAsync("study_source", study.RootElement, CancellationToken.None);
            await AwaitApprovalAsync(permissions).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
            AssistantToolResult studied = await sourceResult.ConfigureAwait(true);
            using var sourceCounts = JsonDocument.Parse(studied.Text);
            if (sourceCounts.RootElement.GetProperty("Imported").GetInt32() != 3 || File.Exists(Path.Combine(repository, "never.txt")))
            { throw new InvalidOperationException("Native source acquisition did not remain a read-only study of the selected repository."); }
            if (!(await brain.SearchAsync("inspect value").ConfigureAwait(true)).Any(hit => hit.Note.Source == "external_source")) { throw new InvalidOperationException("Imported source was not recoverable."); }
            results.Add("Actual study_source adapter required exact reinforced source approval, recovered TypeScript knowledge and declared dependency metadata without executing postinstall or installing anything.");
            if (args.Contains("--brain-live-smoke", StringComparer.Ordinal))
            {
                var live = services.GetRequiredService<GeminiLiveClient>(); if (!live.IsAvailable) { throw new InvalidOperationException("A configured model credential is required for live diagnostics."); }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                const string first = "Quando estou programando, fico muito mais confortável no Example Editor do que nos outros.";
                stage = "live_natural_preference";
                await live.AskAsync(new(first, CultureInfo.GetCultureInfo("pt-BR"), "Synthetic learner"), timeout.Token).ConfigureAwait(true);
                BrainNote original = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == "owner.editor" && note.Title == "Example Editor")
                    ?? throw new InvalidOperationException("The actual model did not learn the natural synthetic editor preference.");
                results.Add("Actual configured model interpreted a natural Portuguese preference, proposed verbatim grounded evidence and created the expected Markdown memory.");
                const string correction = "Na verdade, troquei para Reviewed Editor e agora prefiro ele.";
                stage = "live_correction";
                await live.AskAsync(new(correction, CultureInfo.GetCultureInfo("pt-BR"), "Synthetic learner"), timeout.Token).ConfigureAwait(true);
                if (!brain.Snapshot.Notes.Any(note => note.IsKnowledge && note.Id == original.Id && note.Title == "Reviewed Editor")
                    || !brain.Snapshot.Notes.Any(note => note.Status == "archived" && note.Body.Contains("Example Editor", StringComparison.Ordinal)))
                { throw new InvalidOperationException("Actual model correction did not preserve stable identity and previous evidence."); }
                stage = "live_recall";
                string? answer = await live.AskAsync(new("Qual é o editor que eu prefiro agora?", CultureInfo.GetCultureInfo("pt-BR"), "Synthetic learner"), timeout.Token).ConfigureAwait(true);
                if (answer?.Contains("Reviewed Editor", StringComparison.OrdinalIgnoreCase) != true) { throw new InvalidOperationException("Actual model did not recover the corrected preference."); }
                results.Add("Actual model recalled the real target ID, corrected the preference with history and recovered Reviewed Editor in a subsequent turn.");
                if (permissions.PendingRequests.Count != 0) { throw new InvalidOperationException("Cognitive diagnostics attempted an unrelated protected action."); }
            }
            await services.GetRequiredService<BrainLearning>().FlushAsync().ConfigureAwait(true);
            await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), PassedHeader.Concat(results)).ConfigureAwait(true);
        }
        catch (Exception error)
        { await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), new[] { "FAIL: " + stage + " (" + error.GetType().Name + "). No raw provider error or credential was logged." }.Concat(results)).ConfigureAwait(true); }
        finally { services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
    private static async Task AwaitApprovalAsync(PermissionManager permissions, PermissionRisk risk = PermissionRisk.Critical)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (permissions.PendingRequests.Count == 0) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
        if (permissions.PendingRequests.Count != 1 || permissions.PendingRequests[0].Risk != risk) { throw new InvalidOperationException("Expected one exact process approval of the specified risk."); }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Permissions;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in public source and actual Gemini diagnostics, using isolated storage, muted audio and no microphone or repository execution.</summary>
internal static class BrainGitHubSmokeRunner
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private sealed record Evidence(int Process, string Vault, IReadOnlyList<Version> Versions, string Query, string ExpectedId);
    private sealed record Version(string Id, string Path, string Hash);

    /// <summary>Acquires public sources through production adapters/native approval UI, or verifies their recovery in a second process.</summary>
    internal static async Task<IReadOnlyList<string>> RunAsync(IServiceProvider services, string directory)
    {
        string[] args = Environment.GetCommandLineArgs(); var results = new List<string>();
        var brain = services.GetRequiredService<BrainService>(); var memory = services.GetRequiredService<IConversationMemory>();
        var tools = services.GetRequiredService<IAssistantTools>(); var permissions = services.GetRequiredService<PermissionManager>();
        var center = services.GetRequiredService<WindowsApprovalCenter>(); var settings = services.GetRequiredService<ISettingsService>();
        await brain.InitializeAsync().ConfigureAwait(true);
        string manifestPath = Path.Combine(directory, "github-restart-manifest.json");
        if (args.Contains("--github-restart", StringComparer.Ordinal))
        {
            Evidence manifest = JsonSerializer.Deserialize<Evidence>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(true), Json) ?? throw new IOException("Missing public source restart evidence.");
            if (manifest.Process == Environment.ProcessId || manifest.Vault != FileBrainStorage.Hash(brain.Location)) { throw new InvalidOperationException("Restart requires the same vault in a new process."); }
            try { using Process previous = Process.GetProcessById(manifest.Process); if (!previous.HasExited) { throw new InvalidOperationException("Previous diagnostic process must exit first."); } } catch (ArgumentException) { }
            await brain.RefreshAsync(rebuild: true).ConfigureAwait(true);
            foreach (Version version in manifest.Versions)
            { if (!brain.Snapshot.Notes.Any(note => note.Id == version.Id && note.Path == version.Path && note.Hash == version.Hash)) { throw new IOException("Persisted public source evidence changed across restart."); } }
            if (!(await brain.SearchAsync(manifest.Query, 20).ConfigureAwait(true)).Any(hit => hit.Note.Id == manifest.ExpectedId)) { throw new IOException("Public source knowledge was not recovered after restart."); }
            if (permissions.PendingRequests.Count != 0 || permissions.Grants.Count != 0) { throw new IOException("Restart retained a pending or remembered source authorization."); }
            return ["Actual new process " + Environment.ProcessId + " recovered " + manifest.Versions.Count + " exact Markdown IDs/paths/hashes with a rebuilt index; original process " + manifest.Process + " had exited.",
                "Public source query recovered the recorded source ID/provenance without network acquisition, code execution or an authorization replay."];
        }
        bool liveMode = args.Contains("--brain-live-smoke", StringComparer.Ordinal);
        if (liveMode)
        {
            ((MauiAssistantTools)tools).PublicSourceToolObserved += (name, arguments, result) => File.AppendAllText(Path.Combine(directory, "live-tool-calls.jsonl"),
                JsonSerializer.Serialize(new { tool = name, arguments, status = result.Failed && result.Status == "success" ? "failed" : result.Status, result.Failed, result = result.Text }) + "\n");
            await settings.UpdateAsync(settings.Current with { GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Timeout = TimeSpan.FromMinutes(6) } }).ConfigureAwait(true);
            var live = services.GetRequiredService<GeminiLiveClient>(); if (!live.IsAvailable) { throw new InvalidOperationException("Existing explicitly configured protected Gemini credential is required."); }
            const string request = "Laura, estude e aprenda essa habilidade: https://github.com/morluto/rea";
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            Task<string?> conversation = live.AskAsync(new(request, CultureInfo.GetCultureInfo("pt-BR"), "Synthetic repository learner"), deadline.Token);
            await ApprovePublicSourcesAsync(conversation, permissions, center, deadline.Token).ConfigureAwait(true);
            string? answer = await conversation.ConfigureAwait(true);
            object observed = ((MauiAssistantTools)tools).ObservedTaskForSmoke(memory.WorkingContext.TurnId);
            await File.WriteAllTextAsync(Path.Combine(directory, "live-metrics.json"), JsonSerializer.Serialize(new { live.LastTaskContinuationCount, live.AudioBytesReceived, live.LastFailureCategory,
                operationallyValidated = false, skillProposals = brain.Snapshot.Notes.Count(note => note.Type == "skill" && note.Status == "draft") }, Json), deadline.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "live-task.json"), JsonSerializer.Serialize(observed, Json), deadline.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "live-answer.txt"), answer ?? "No completed model response.", deadline.Token).ConfigureAwait(true);
            using var taskState = JsonDocument.Parse(JsonSerializer.Serialize(observed));
            if (!taskState.RootElement.TryGetProperty("observations", out JsonElement observations)
                || !observations.EnumerateArray().Any(item => item.GetProperty("Tool").GetString() == "study_github_repository" && item.GetProperty("Status").GetString() == "success")
                || !brain.Snapshot.Notes.Any(note => note.Source == "external_github"))
            { throw new InvalidOperationException("Actual Gemini did not confirm repository acquisition; inspect retained live task evidence."); }
            if (taskState.RootElement.GetProperty("sourceAssessmentPending").GetBoolean()
                || !observations.EnumerateArray().Any(item => item.GetProperty("Tool").GetString() == "read_brain_passage")
                || !observations.EnumerateArray().Any(item => item.GetProperty("Tool").GetString() == "list_brain_skills"))
            { throw new InvalidOperationException("Actual Gemini acquired files but did not finish reading and source/skill assessment within the native continuation budget."); }
            results.Add("Actual Gemini Live received the original Portuguese request and selected study_github_repository; native adapters acquired and persisted public files. No dispatcher selection was simulated.");
            results.Add("Model-selected tools and observed results are retained in live-task.json; its answer is live-answer.txt. Skill proposals count only if the actual draft exists.");
        }
        else
        {
            foreach (string url in new[] { "https://github.com/morluto/rea", "https://github.com/dotnet/ClangSharp" })
            {
                string topic = "Public study " + url.Split('/')[^1]; memory.SetWorkingContext("Study this public repository as untrusted knowledge: " + url);
                using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { url, topic }));
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                Task<AssistantToolResult> acquisition = tools.ExecuteAsync("study_github_repository", request.RootElement, deadline.Token);
                await ApprovePublicSourcesAsync(acquisition, permissions, center, deadline.Token).ConfigureAwait(true);
                AssistantToolResult acquired = await acquisition.ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, url.Split('/')[^1] + "-acquisition.json"), acquired.Text, deadline.Token).ConfigureAwait(true);
                if (acquired.Failed) { throw new InvalidOperationException("Public repository adapter reported failure."); }
                BrainRemoteAcquisitionResult actual = JsonSerializer.Deserialize<BrainRemoteAcquisitionResult>(acquired.Text, Json) ?? throw new IOException("Missing acquisition evidence.");
                if (actual.Imported + actual.Reused == 0 || actual.Revision.Length != 40) { throw new IOException("Public acquisition imported no verified source bytes."); }
                results.Add(url + " @ " + actual.Revision + ": " + actual.Imported + " imported, " + actual.Reused + " reused, " + actual.Excluded + " excluded; "
                    + actual.InventoryFiles + " inventoried, " + actual.HttpRequests + " HTTP requests, " + actual.ReceivedBytes + " bytes, " + actual.ElapsedMilliseconds + " ms. Partial acquisition, no execution.");
            }
            BrainNote source = brain.Snapshot.Notes.First(note => note.Source == "external_github" && note.Title.EndsWith("README.md", StringComparison.Ordinal));
            int marker = source.Body.IndexOf("--- Source data ---\n", StringComparison.Ordinal); string text = source.Body[(marker + "--- Source data ---\n".Length)..].Trim();
            string quote = text[..Math.Min(500, text.Length)];
            var definition = new BrainSkillDefinition("mcp", "Evaluate a source-documented repository procedure against actual available tools before execution.",
                ["A task requests a procedure documented in the studied repository."], [], ["runtime:node", "mcp:studied_repository"], ["mcp.studied_repository.inspect"],
                ["Every external operation requires separate native approval."], ["Recover source documentation.", "Check actual server/tool availability.", "Validate the intended scope before any execution."],
                ["Documentation acquisition is partial; no external procedure was tested."], ["Observe results of an independently authorized execution before claiming operational validation."],
                ["Report absent tools and remain inert."], ["repository procedure readiness assessment"], [new(source.Id, source.Hash, quote)], []);
            using var proposal = JsonDocument.Parse(JsonSerializer.Serialize(new { proposal = JsonSerializer.Serialize(new BrainSkillProposal("Public repository procedure assessment", "Assess requirements from studied sources without claiming operational mastery.", definition)) }));
            AssistantToolResult proposed = await tools.ExecuteAsync("propose_brain_skill", proposal.RootElement, CancellationToken.None).ConfigureAwait(true);
            if (proposed.Failed) { throw new IOException("Evidence-grounded public procedure proposal failed."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "proposal.json"), proposed.Text).ConfigureAwait(true);
            results.Add("The diagnostic invoked propose_brain_skill with actual source ID/hash/quote. SKILL.md remains draft, unapproved and blocked by absent Node/MCP requirements. This is adapter integration evidence, not a model-authored procedure or tested external skill.");
        }
        BrainNote recoveredSource = brain.Snapshot.Notes.First(note => note.Source == "external_github" && note.Title.EndsWith("README.md", StringComparison.Ordinal));
        string query = recoveredSource.Title;
        if (!(await brain.SearchAsync(query, 20).ConfigureAwait(true)).Any(hit => hit.Note.Id == recoveredSource.Id)) { throw new IOException("Acquired public knowledge not recoverable in current process."); }
        BrainNote[] selected = brain.Snapshot.Notes.Where(note => note.IsKnowledge || note.Type == "skill" && note.Status == "draft").ToArray();
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new Evidence(Environment.ProcessId, FileBrainStorage.Hash(brain.Location),
            selected.Select(note => new Version(note.Id, note.Path, note.Hash)).ToArray(), query, recoveredSource.Id), Json)).ConfigureAwait(true);
        results.Add("Exact retained source/proposal versions and a recovery query saved for a separate process restart. License/attribution and source bytes are in isolated Brain Markdown.");
        return results;
    }

    /// <summary>Invokes real native approval controls only for the diagnostic's bounded public GitHub scopes; denies any unrelated action.</summary>
    private static async Task ApprovePublicSourcesAsync(Task operation, PermissionManager permissions, WindowsApprovalCenter center, CancellationToken token)
    {
        var seen = new HashSet<Guid>();
        while (!operation.IsCompleted)
        {
            foreach (ApprovalRequest request in permissions.PendingRequests.Where(request => !seen.Contains(request.Id)))
            {
                seen.Add(request.Id); await Task.Delay(250, token).ConfigureAwait(true);
                if (request.Action.Origin is not ("GitHub source inventory" or "GitHub source acquisition")) { center.InvokeForSmoke("deny"); continue; }
                if (request.CanRemember || seen.Count > 12) { throw new InvalidOperationException("Unexpected public-source approval scope or count."); }
                if (request.Risk == PermissionRisk.Critical) { center.ConfirmCriticalForSmoke(); } else { center.InvokeForSmoke("once"); }
            }
            await Task.Delay(40, token).ConfigureAwait(true);
        }
    }
}

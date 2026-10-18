using System.Diagnostics;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Exercises a real automatic Gemini-to-ChatGPT handoff using only two synthetic Brain notes and native read-only tools.</summary>
internal static class ProviderHandoffSmokeRunner
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    /// <summary>Verifies recipient-scoped reads, shared history and stable task identity across independently bounded rounds.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--included-plan-only-confirmed", StringComparer.Ordinal))
        { throw new InvalidOperationException("Explicit included-plan-only confirmation is required."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var token = timeout.Token;
        var account = await services.GetRequiredService<IChatGptConnection>().ReadAsync(token).ConfigureAwait(true);
        if (account.Status != "plan_authorized" || !services.GetRequiredService<IChatGptInference>().IsAvailable)
        { throw new InvalidOperationException("The verified service-backed ChatGPT session is required."); }
        var settings = services.GetRequiredService<ISettingsService>(); var brain = services.GetRequiredService<BrainService>();
        string storage = services.GetRequiredService<AssistantStorage>().Directory;
        if (!PermissionPolicy.Within(brain.Location, storage)) { throw new InvalidOperationException("Handoff diagnostics require the isolated synthetic Brain."); }
        var first = await brain.SaveAsync(new BrainNote { Path = "Knowledge/Facts/HandoffA.md", Title = "Synthetic stage A",
            Body = "The independently observed synthetic stage A code is ALPHA-317.", Source = "synthetic_handoff" }, token).ConfigureAwait(true);
        var second = await brain.SaveAsync(new BrainNote { Path = "Knowledge/Facts/HandoffB.md", Title = "Synthetic stage B",
            Body = "The independently observed synthetic stage B code is BETA-731.", Source = "synthetic_handoff" }, token).ConfigureAwait(true);
        var paths = new[] { first, second }.Select(note => Path.GetFullPath(Path.Combine(brain.Location, note.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await settings.UpdateAsync(settings.Current with { ProviderSelection = ConversationProviderSelection.Automatic, MicrophoneMuted = true, Voice = settings.Current.Voice with { Volume = 0 },
            GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Timeout = TimeSpan.FromMinutes(1), LiveVoice = "Charon" },
            Brain = settings.Current.Brain with { AutoLearn = false }, Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Ask },
            ChatGpt = new() { Enabled = true, AllowAutomatic = true, ShareContext = true, IncludedPlanOnly = true, Registration = account.Active, Model = account.VerifiedModel },
            CognitiveTasks = new() { MaximumCycles = 4, MaximumToolCalls = 20, MaximumProviderCalls = 12, ActiveMinutes = 4 } }, token).ConfigureAwait(true);
        var tools = (MauiAssistantTools)services.GetRequiredService<IAssistantTools>(); var memory = services.GetRequiredService<IConversationMemory>();
        var observations = new List<HandoffObservation>();
        void Observe(string name, JsonElement arguments, AssistantToolResult result)
        {
            string id = name == "read_brain_passage" && arguments.TryGetProperty("id", out var value) ? value.GetString() ?? "" : "";
            observations.Add(new(name, ProviderExecutionScope.Provider ?? "", memory.WorkingContext.TurnId, id, result.Failed));
        }
        tools.IsolatedAgentToolObserved += Observe;
        var engine = services.GetRequiredService<IAssistantEngine>(); var tasks = services.GetRequiredService<CognitiveTasks>();
        var permissions = services.GetRequiredService<PermissionManager>(); int approvals = 0;
        try
        {
            Task request = engine.SubmitCommandAsync("Investigate two synthetic evidence stages using TWO independent inference rounds of the SAME task. "
                + "In the FIRST round, use plan_task with tools=[\"read_brain_passage\",\"list_capabilities\",\"read_brain_passage\",\"list_capabilities\"], "
                + "then read ONLY stage A using read_brain_passage and return its code immediately. The native coordinator will continue the remaining plan automatically; do not complete later stages in this first round. "
                + "In the NEXT round, inspect list_capabilities, read stage B, inspect list_capabilities again, then explain BOTH observed codes using shared history and current evidence. "
                + "Stage A metadata: " + JsonSerializer.Serialize(new { first.Id, first.Hash }) + "; stage B metadata: " + JsonSerializer.Serialize(new { second.Id, second.Hash })
                + ". Use start=0,length=1000 for passage reads. No commands, skills, external sources or personal data are needed.", token);
            var elapsed = Stopwatch.StartNew(); int settled = 0;
            while (settled < 15)
            {
                Guid id = memory.WorkingContext.TurnId; var current = tasks.Current(id);
                bool active = !request.IsCompleted || engine.PendingMessageCount > 0 || current?.Rounds is { Outcome: "running" } or { NeedsContinuation: true, CanContinue: true };
                settled = active ? 0 : settled + 1;
                foreach (var review in permissions.PendingRequests)
                {
                    bool allow = review.Action.TaskId == id && review.Action.Operation == PermissionOperation.FileReadCloud
                        && review.Action.Destination is "api.openai.com" or "generativelanguage.googleapis.com"
                        && review.Action.Resources.Length > 0 && review.Action.Resources.All(path => paths.Contains(Path.GetFullPath(path)));
                    permissions.Decide(review.Id, allow ? ApprovalChoice.Once : ApprovalChoice.Deny); if (allow) { approvals++; }
                }
                if (elapsed.Elapsed > TimeSpan.FromMinutes(4)) { await tasks.PauseAsync(id).ConfigureAwait(true); break; }
                await Task.Delay(100, token).ConfigureAwait(true);
            }
            await request.ConfigureAwait(true);
            var identity = observations.Select(item => item.TaskId).Distinct().ToArray();
            var task = identity.Length == 1 ? tasks.Current(identity[0]) : null;
            string answer = engine.ConversationHistory.LastOrDefault(item => item.Source == ConversationMessageSource.Assistant)?.Text ?? "";
            bool passed = identity.Length == 1 && observations.Any(item => item.Provider == "gemini" && item.Note == first.Id && !item.Failed)
                && observations.Any(item => item.Provider == "chatgpt" && item.Note == second.Id && !item.Failed)
                && task?.Rounds is { Cycles: >= 2, Provider: "chatgpt" } && task.Steps.All(step => step.State != "pending")
                && answer.Contains("ALPHA-317", StringComparison.Ordinal) && answer.Contains("BETA-731", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory, "handoff.json"), JsonSerializer.Serialize(new { Passed = passed, RealProviders = true,
                NativeObservations = observations, DisclosureApprovals = approvals, StableTaskIdentity = identity.Length == 1, task?.Rounds,
                PendingStages = task?.Steps.Count(step => step.State == "pending"), PersonalBrainUsed = false, MicrophoneUsed = false, PaidApiKeyUsed = false }, EvidenceJson), token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "answer.md"), answer, token).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "handoff.png")).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), passed ? "PASS: actual Gemini and ChatGPT completed the same synthetic task across independent rounds with authorized Brain reads and shared history."
                : "PARTIAL: a real cross-provider handoff was not fully verified; see actual observations.", token).ConfigureAwait(true);
        }
        finally { tools.IsolatedAgentToolObserved -= Observe; services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }

    /// <summary>Safe observation metadata excludes tool arguments, note bodies and credentials.</summary>
    private sealed record HandoffObservation(string Tool, string Provider, Guid TaskId, string Note, bool Failed);
}

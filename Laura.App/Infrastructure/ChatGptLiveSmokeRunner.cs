using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Exercises the real account provider through the actual assistant queue and native tools, using isolated Brain and history.</summary>
internal static class ChatGptLiveSmokeRunner
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    /// <summary>Verifies a fixed read-only native tool round without accessing personal history or recording credentials.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--included-plan-only-confirmed", StringComparer.Ordinal))
        { throw new InvalidOperationException("The owner must confirm this application's additional-credit allowance is zero before real inference."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var connection = services.GetRequiredService<IChatGptConnection>(); var state = await connection.ReadAsync(timeout.Token).ConfigureAwait(true);
        if (state.Status != "plan_authorized") { throw new InvalidOperationException("Real ChatGPT plan authorization is unavailable."); }
        var models = await connection.ModelsAsync(timeout.Token).ConfigureAwait(true);
        string model = models.FirstOrDefault(item => item.Slug == state.VerifiedModel)?.Slug ?? models[0].Slug;
        if (!services.GetRequiredService<IChatGptInference>().IsAvailable) { await connection.ProbeAsync(model, true, timeout.Token).ConfigureAwait(true); }
        var settings = services.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(settings.Current with { ProviderSelection = ConversationProviderSelection.ChatGpt,
            ChatGpt = new() { Enabled = true, IncludedPlanOnly = true, Model = model, ShareContext = true, Registration = state.Active }, MicrophoneMuted = true }).ConfigureAwait(true);
        var tools = (MauiAssistantTools)services.GetRequiredService<IAssistantTools>();
        var observed = new List<string>(); var identities = new List<Guid>(); bool failed = false;
        void Observe(string name, JsonElement arguments, AssistantToolResult result)
        { observed.Add(name); identities.Add(services.GetRequiredService<IConversationMemory>().WorkingContext.TurnId); failed |= result.Failed; }
        tools.IsolatedAgentToolObserved += Observe;
        var engine = services.GetRequiredService<IAssistantEngine>();
        try
        {
            await engine.SubmitCommandAsync("Use the native list_capabilities tool exactly once. Then report that you inspected Laura's actual registered capabilities. Do not execute any other operation or propose skills.", timeout.Token).ConfigureAwait(true);
            var tasks = await services.GetRequiredService<CognitiveTasks>().ListAsync(timeout.Token).ConfigureAwait(true);
            if (identities.Count == 0)
            {
                var failure = services.GetRequiredService<Laura.Core.Conversation.ChatGptConversationEngine>().LastFailure;
                await File.WriteAllTextAsync(Path.Combine(directory, "live-failure.json"), JsonSerializer.Serialize(new { Verified = false, Failure = failure, NativeToolsObserved = 0 }, EvidenceJson), timeout.Token).ConfigureAwait(true);
                throw new InvalidOperationException("Real inference did not dispatch the expected native capability tool; see safe failure metadata.");
            }
            var task = tasks.Single(item => identities.Contains(item.Id));
            bool success = !failed && observed.Count == 1 && observed[0] == "list_capabilities" && identities.Distinct().Count() == 1
                && engine.ConversationHistory.Count > 0 && engine.ConversationHistory[^1].Source == Laura.Core.Conversation.ConversationMessageSource.Assistant && task.Rounds?.Provider == "chatgpt";
            await File.WriteAllTextAsync(Path.Combine(directory, "live-result.json"), JsonSerializer.Serialize(new { Success = success, Model = model, RealAuthorization = true,
                ActualNativeTools = observed, StableTaskIdentity = identities.Distinct().Count() == 1, task.Rounds,
                PersonalBrainUsed = false, MicrophoneUsed = false, PaidApiKeyUsed = false }, EvidenceJson), timeout.Token).ConfigureAwait(true);
            if (!success) { throw new InvalidOperationException("Real shared-tool inference was not verified."); }
            await Task.Delay(500, timeout.Token).ConfigureAwait(true); await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "live-native-tools.png")).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: real ChatGPT inference completed through AssistantEngine, shared native capability tool, stable task identity and isolated Brain/history.", timeout.Token).ConfigureAwait(true);
        }
        finally { tools.IsolatedAgentToolObserved -= Observe; services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
}

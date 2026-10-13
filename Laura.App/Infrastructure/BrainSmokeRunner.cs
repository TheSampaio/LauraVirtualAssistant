using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Permissions;
using Laura.Platform.Windows.Conversation;
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
}

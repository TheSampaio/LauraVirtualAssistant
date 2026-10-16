using System.Diagnostics;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Laura.Platform.Windows.Conversation;

namespace Laura.App.Infrastructure;

/// <summary>Verifies the full native catalogue, actual local tool dispatch and private durable receipts through the assistant queue.</summary>
internal static class LocalToolsSmokeRunner
{
    private static readonly JsonSerializerOptions DiagnosticJson = new() { WriteIndented = true };
    /// <summary>Runs isolated EN/PT read-only system checks without credentials, cloud traffic, audio playback or capture.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Local tool diagnostics require isolated UI smoke mode."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var settings = services.GetRequiredService<ISettingsService>(); var original = settings.Current;
        var inference = services.GetRequiredService<ILocalInferenceEngine>(); var engine = services.GetRequiredService<IAssistantEngine>();
        var tools = (MauiAssistantTools)services.GetRequiredService<IAssistantTools>(); var tasks = services.GetRequiredService<CognitiveTasks>();
        var observed = new List<(string Name, Guid Turn, string Status)>(); int leaked = 0;
        bool finished = false; int ticks = 0; double maximumGapMs = 0; long previous = Stopwatch.GetTimestamp();
        window.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
        {
            if (finished) { return false; } long now = Stopwatch.GetTimestamp();
            maximumGapMs = Math.Max(maximumGapMs, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; ticks++; return true;
        });
        Action<string, Guid, string> observe = (name, turn, status) => observed.Add((name, turn, status));
        EventHandler<string> leak = (_, _) => Interlocked.Increment(ref leaked);
        tools.LocalSmokeToolObserved += observe; inference.TokenReceived += leak;
        var measured = new List<object>();
        try
        {
            await settings.UpdateAsync(original with { MicrophoneMuted = true, ClapWakeEnabled = false, GreetOnStartup = false,
                Voice = original.Voice with { Volume = 0 }, GenerativeAi = original.GenerativeAi with
                { Enabled = true, Provider = "local", LocalModel = SupportedModels.Intelligence.Id, LocalGpuLayers = -1 } }, timeout.Token).ConfigureAwait(true);
            await inference.LoadAsync(SupportedModels.Intelligence.Id, timeout.Token).ConfigureAwait(true);
            foreach (var (culture, prompt) in new[] { ("en-US", "Use system_status now and report actual measured available RAM. Do not estimate or use prior messages."),
                ("pt-BR", "Use system_status agora e informe a RAM disponível realmente medida. Não estime nem use mensagens anteriores.") })
            {
                await settings.UpdateAsync(settings.Current with { Culture = culture }, timeout.Token).ConfigureAwait(true);
                int before = observed.Count; var watch = Stopwatch.StartNew();
                await engine.SubmitContextCommandAsync(prompt, "Synthetic native tool check " + culture, timeout.Token).ConfigureAwait(true);
                var working = services.GetRequiredService<IConversationMemory>().WorkingContext;
                var checkpoint = tasks.Current(working.TurnId); var answer = engine.ConversationHistory.LastOrDefault(message => message.Source == ConversationMessageSource.Assistant);
                bool nativeVerified = observed.Skip(before).Any(item => item.Name == "system_status" && item.Turn == working.TurnId && item.Status == "success")
                    && checkpoint?.Operations.Any(operation => operation.Tool == "system_status" && operation.Status == "success") == true;
                measured.Add(new { culture, elapsedMs = watch.Elapsed.TotalMilliseconds, nativeCalls = observed.Count - before, nativeVerified,
                    answerPresent = !string.IsNullOrWhiteSpace(answer?.Text), failure = services.GetRequiredService<ConversationProviderRouter>().LastFailure?.Kind.ToString() });
                await File.WriteAllTextAsync(Path.Combine(directory, "local-tools-attempt.json"), JsonSerializer.Serialize(measured, DiagnosticJson), timeout.Token).ConfigureAwait(true);
                if (!nativeVerified || string.IsNullOrWhiteSpace(answer?.Text) || leaked != 0) { throw new InvalidOperationException("Actual local catalogue selection, native receipt or private stream isolation failed."); }
            }
            finished = true;
            if (ticks < 5 || maximumGapMs > 1000 || services.GetRequiredService<WindowsClapListener>().IsCapturing
                || services.GetRequiredService<LocalVoiceSession>().IsRunning || services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>().IsListening)
            { throw new InvalidOperationException("Local native tools lost dispatcher responsiveness or opened capture."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "local-tools.json"), JsonSerializer.Serialize(new
            { inference.Backend, inference.OffloadedLayers, measured, privateStreamIsolation = leaked == 0, ticks, maximumGapMs,
                actualTaskReceipts = true, cloudRequest = false, microphoneCapture = false, audioPlayback = false }, DiagnosticJson), timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Actual EN/PT native system_status calls selected from the full catalogue through the engine queue, with private task receipts and no leaked tool arguments, cloud, microphone or playback.", timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            finished = true; tools.LocalSmokeToolObserved -= observe; inference.TokenReceived -= leak;
            await inference.UnloadAsync(CancellationToken.None).ConfigureAwait(true); await settings.UpdateAsync(original).ConfigureAwait(true);
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Models;
using Laura.Core.Conversation;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Verifies normal application composition, queued local generation and dispatcher progress with isolated pinned weights.</summary>
internal static class LocalBackendSmokeRunner
{
    private static readonly JsonSerializerOptions DiagnosticJson = new() { WriteIndented = true };
    /// <summary>Loads the actual automatic GPU backend, submits a synthetic context turn and renders native backend controls without audio capture.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Local backend diagnostics require isolated UI smoke mode."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var settings = services.GetRequiredService<ISettingsService>(); var original = settings.Current;
        var inference = services.GetRequiredService<ILocalInferenceEngine>(); var engine = services.GetRequiredService<IAssistantEngine>();
        bool finished = false; long previous = Stopwatch.GetTimestamp(); int ticks = 0; double maximumGapMs = 0;
        window.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
        {
            if (finished) { return false; }
            long now = Stopwatch.GetTimestamp(); maximumGapMs = Math.Max(maximumGapMs, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
            previous = now; ticks++; return !finished;
        });
        try
        {
            await settings.UpdateAsync(original with { MicrophoneMuted = true, ClapWakeEnabled = false, GreetOnStartup = false,
                Voice = original.Voice with { Volume = 0 }, GenerativeAi = original.GenerativeAi with
                { Enabled = true, Provider = "local", LocalModel = SupportedModels.Intelligence.Id, LocalGpuLayers = -1 } }, timeout.Token).ConfigureAwait(true);
            await inference.LoadAsync(SupportedModels.Intelligence.Id, timeout.Token).ConfigureAwait(true);
            if (inference.Backend != "cuda12" || inference.OffloadedLayers <= 0) { throw new InvalidOperationException("Application composition did not load its admitted app-local CUDA backend."); }
            await engine.SubmitContextCommandAsync("Answer only with the number: what is 2 + 2?", "Synthetic local backend check", timeout.Token).ConfigureAwait(true);
            var answer = engine.ConversationHistory.LastOrDefault(message => message.Source == ConversationMessageSource.Assistant);
            if (answer is null || !answer.Text.Contains('4')) { throw new InvalidOperationException("The actual queued local provider did not return its synthetic arithmetic result."); }
            finished = true;
            if (services.GetRequiredService<WindowsClapListener>().IsCapturing || services.GetRequiredService<LocalVoiceSession>().IsRunning
                || services.GetRequiredService<GeminiLiveClient>().IsListening)
            { throw new InvalidOperationException("Local backend smoke unexpectedly opened audio capture."); }
            var navigation = (NavigationPage)window.Page!;
            foreach (string culture in new[] { "pt-BR", "en-US" })
            {
                services.GetRequiredService<IUiLocalizer>().SetCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
                var setup = services.GetRequiredService<SetupPage>(); await navigation.PushAsync(setup, false).ConfigureAwait(true);
                await setup.ShowProviderForSmokeAsync("local").ConfigureAwait(true);
                window.Width = 720; window.Height = 600; await Task.Delay(200, timeout.Token).ConfigureAwait(true);
                await setup.VerifySettingsScrollForSmokeAsync(true, .3).ConfigureAwait(true);
                await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "loaded-backend-" + culture + ".png")).ConfigureAwait(true);
                await navigation.PopAsync(false).ConfigureAwait(true);
            }
            if (ticks < 5 || maximumGapMs > 1000) { throw new InvalidOperationException("The native dispatcher did not remain responsive during backend work."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "local-backend.json"), JsonSerializer.Serialize(new
            { inference.Backend, inference.BackendStatus, inference.OffloadedLayers, inference.FreeGpuMemoryBeforeLoad, ticks, maximumGapMs,
                queuedGenerationVerified = true, measurementScope = "model_load_and_queued_generation", microphoneCapture = false, cloudRequest = false, audioPlayback = false }, DiagnosticJson), timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Actual MAUI composition loaded admitted CUDA weights, generated a verified synthetic answer through the engine queue, preserved dispatcher progress and rendered loaded backend controls in EN/PT at 720x600. No cloud, microphone or audio playback.", timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            finished = true; await inference.UnloadAsync(CancellationToken.None).ConfigureAwait(true);
            await settings.UpdateAsync(original).ConfigureAwait(true);
        }
    }
}

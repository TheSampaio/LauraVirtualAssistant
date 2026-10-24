using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Permissions;
using Laura.Core.Skills;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Tests explicitly requested real speaker output from local and authorized GPT text, with isolated history and no microphone.</summary>
internal static class SpokenProvidersSmokeRunner
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    /// <summary>Measures native rendering responsiveness and verifies actual provider results reach the Windows playback device.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var settings = services.GetRequiredService<ISettingsService>();
        var permissions = services.GetRequiredService<PermissionManager>();
        void DenyEffects(object? sender, EventArgs args)
        { foreach (var pending in permissions.PendingRequests) { permissions.Decide(pending.Id, ApprovalChoice.Deny); } }
        permissions.Changed += DenyEffects;
        var evidence = new List<object>();
        try
        {
            await settings.UpdateAsync(settings.Current with { MicrophoneMuted = true, ClapWakeEnabled = false, GreetOnStartup = false, AnnounceHourly = false,
                GenerativeAi = settings.Current.GenerativeAi with { Enabled = false, Timeout = TimeSpan.FromMinutes(4) }, Voice = settings.Current.Voice with { Volume = 65 } }, timeout.Token).ConfigureAwait(true);
            await Task.Delay(1500, timeout.Token).ConfigureAwait(true);
            var intervals = new List<double>(); long previous = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < 100; frame++)
            { await Task.Delay(20, timeout.Token).ConfigureAwait(true); long now = Stopwatch.GetTimestamp(); intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; }
            intervals.Sort();
            evidence.Add(new { renderer = "Native", dispatcherMedianMs = intervals[50], dispatcherP95Ms = intervals[95], sampleCount = intervals.Count });
            await File.WriteAllTextAsync(Path.Combine(directory, "renderers.json"), JsonSerializer.Serialize(evidence, EvidenceJson), timeout.Token).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "native-hud.png")).ConfigureAwait(true);
            var dispatcher = services.GetRequiredService<ISkillDispatcher>();
            var router = services.GetRequiredService<ConversationProviderRouter>();
            var speech = services.GetRequiredService<ISpeechSynthesizer>();
            foreach (var provider in new[] { ConversationProviderSelection.Local, ConversationProviderSelection.ChatGpt })
            {
                if (provider == ConversationProviderSelection.ChatGpt)
                {
                    var state = await services.GetRequiredService<IChatGptConnection>().ReadAsync(timeout.Token).ConfigureAwait(true);
                    await File.WriteAllTextAsync(Path.Combine(directory, "gpt-authorization.json"), JsonSerializer.Serialize(new { state.Status, state.Error, state.HttpStatus, state.IncludedPlanOnlyConfirmed, state.VerifiedModel }), timeout.Token).ConfigureAwait(true);
                    if (state.Status != "plan_authorized" || !state.IncludedPlanOnlyConfirmed || string.IsNullOrWhiteSpace(state.VerifiedModel))
                    { throw new InvalidOperationException("The saved GPT connection lacks verified included-plan authorization; no additional-credit inference was attempted."); }
                    await settings.UpdateAsync(settings.Current with { ChatGpt = settings.Current.ChatGpt with { Enabled = true, IncludedPlanOnly = true, Registration = state.Active, Model = state.VerifiedModel } }, timeout.Token).ConfigureAwait(true);
                }
                await settings.UpdateAsync(settings.Current with { ProviderSelection = provider }, timeout.Token).ConfigureAwait(true);
                string prompt = provider == ConversationProviderSelection.Local
                    ? "Responda em português com apenas esta frase, sem ferramentas: A conversa local está pronta e eu estou falando com minha voz brasileira."
                    : "Responda em português com apenas esta frase, sem ferramentas: A conexão GPT está pronta e eu continuo falando com minha voz brasileira.";
                var watch = Stopwatch.StartNew();
                var request = SkillRequest.Create(prompt, CultureInfo.GetCultureInfo("pt-BR"), DateTimeOffset.UtcNow, SkillRequestSource.Text) with { TurnId = Guid.NewGuid() };
                SkillResponse response = await dispatcher.DispatchAsync(request, timeout.Token).ConfigureAwait(true);
                string expected = provider == ConversationProviderSelection.Local ? "local" : "chatgpt";
                if (router.LastFailure is not null || router.ActiveProvider != expected || string.IsNullOrWhiteSpace(response.SpokenText))
                { throw new InvalidOperationException("Requested provider did not produce verified text: " + expected + "/" + router.LastFailure?.Kind); }
                await speech.SpeakAsync(new SpeechRequest(response.SpokenText, settings.Current.Voice, "pt-BR") { OutputDevice = settings.Current.OutputDevice }, timeout.Token).ConfigureAwait(true);
                var playback = speech.LastPlayback;
                if (playback is not { State: SpeechPlaybackState.Completed, ReceivedBytes: > 48000, QueuedBytes: > 48000, DevicePositionBytes: > 48000 })
                { throw new InvalidOperationException("Speech did not complete on the Windows output device."); }
                var runtime = services.GetRequiredService<LlamaCppRuntime>();
                await File.WriteAllTextAsync(Path.Combine(directory, "local-device.json"), JsonSerializer.Serialize(new { runtime.ActiveDevice, runtime.OffloadedLayers, runtime.DedicatedGpuBytes, settings.Current.LocalModel.GpuLayers, playback }), timeout.Token).ConfigureAwait(true);
                if (provider == ConversationProviderSelection.Local && (runtime.ActiveDevice == "CPU" || !(runtime.OffloadedLayers > 0 || runtime.DedicatedGpuBytes > 1024L * 1024 * 1024)))
                { throw new InvalidOperationException("Local inference did not confirm actual GPU layer offload."); }
                evidence.Add(new { provider = expected, answer = response.SpokenText, seconds = watch.Elapsed.TotalSeconds, playback,
                    device = provider == ConversationProviderSelection.Local ? runtime.ActiveDevice : "Authorized included plan", offloadedLayers = provider == ConversationProviderSelection.Local ? runtime.OffloadedLayers : null,
                    dedicatedGpuBytes = provider == ConversationProviderSelection.Local ? runtime.DedicatedGpuBytes : null });
                await File.WriteAllTextAsync(Path.Combine(directory, "spoken-providers.json"), JsonSerializer.Serialize(evidence, EvidenceJson), timeout.Token).ConfigureAwait(true);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: real GPU Qwen and authorized GPT produced text; local female Brazilian voice completed playback on the Windows output device for both. Vulkan presented hardware frames. Microphone stayed muted; isolated Brain/history; no additional-credit inference or native effects.", timeout.Token).ConfigureAwait(true);
        }
        finally { permissions.Changed -= DenyEffects; services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
}

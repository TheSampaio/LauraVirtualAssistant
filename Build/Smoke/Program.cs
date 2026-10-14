using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Models;
using Laura.Core.Skills;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Laura.Platform.Windows.Speech;
using Microsoft.Extensions.Logging.Abstractions;

// Explicit smoke tests use isolated storage and never open the microphone.
if (args.Length == 2 && args[0] == "--inspect-gui-policy")
{
    using var image = File.OpenRead(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { Metadata = Laura.Core.Files.ExecutableMetadata.Read(image), Automatic = WindowsApplicationLaunchPolicy.CanOpenAutomatically(args[1], null) }));
    return 0;
}
if (args.Length == 0) { Console.Error.WriteLine("Usage: Laura.Smoke <isolated-directory> --gemini --key-file <Mark-LV api_keys.json> [--quick] [--vad]"); return 2; }
string directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
await settings.LoadAsync(timeout.Token);
var memory = new SqliteConversationMemory(Path.Combine(directory, "memory.db"));
memory.Initialize();
if (args.Contains("--gemini", StringComparer.Ordinal))
{
    int index = Array.IndexOf(args, "--key-file");
    if (index < 0 || index + 1 >= args.Length) { Console.Error.WriteLine("Provide the existing credential file with --key-file. No credential is printed."); return 2; }
    using JsonDocument config = JsonDocument.Parse(await File.ReadAllTextAsync(args[index + 1], timeout.Token));
    var keys = new ProtectedApiKeyStore(directory);
    keys.Save(config.RootElement.GetProperty("gemini_api_key").GetString() ?? "");
    await using var live = new GeminiLiveClient(settings, keys, memory, Array.Empty<ISkill>(), new SmokeShell());
    live.StatusChanged += (_, status) => Console.WriteLine("Live state: " + status);
    IReadOnlyList<string> selectedVoices = args.Contains("--quick", StringComparer.Ordinal) ? ["Charon"] : GeminiLiveOptions.Voices;
    int selectedVoice = Array.IndexOf(args, "--voice");
    if (selectedVoice >= 0)
    {
        if (selectedVoice + 1 >= args.Length || !GeminiLiveOptions.Voices.Contains(args[selectedVoice + 1], StringComparer.Ordinal))
        { throw new InvalidOperationException("Select one of the configured native Gemini voices."); }
        selectedVoices = [args[selectedVoice + 1]];
    }
    foreach (string voice in selectedVoices)
    {
        await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, RememberConversations = false, Voice = settings.Current.Voice with { Volume = 0 }, GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Provider = "gemini", Model = GeminiLiveOptions.Model, LiveVoice = voice } }, timeout.Token);
        await live.ReconfigureAsync();
        string? answer = await live.AskAsync(new ConversationTurn("Say only: Hello, I am Laura.", CultureInfo.GetCultureInfo("en-US"), "Tester"), timeout.Token);
        if (string.IsNullOrWhiteSpace(answer) || live.AudioBytesReceived == 0) { throw new InvalidOperationException("Live returned no transcript or PCM for " + voice); }
        Console.WriteLine($"Voice {voice}: transcript and {live.AudioBytesReceived} PCM bytes received.");
        if (voice == "Charon")
        {
            long before = live.AudioBytesReceived;
            string? followup = await live.AskAsync(new ConversationTurn("Say only: Ready.", CultureInfo.GetCultureInfo("en-US"), "Tester"), timeout.Token);
            if (string.IsNullOrWhiteSpace(followup) || live.AudioBytesReceived <= before) { throw new InvalidOperationException("Persistent second turn failed."); }
            live.Interrupt();
            string? recovery = await live.AskAsync(new ConversationTurn("Diga apenas: Olá, sou a Laura.", CultureInfo.GetCultureInfo("pt-BR"), "Tester"), timeout.Token);
            if (string.IsNullOrWhiteSpace(recovery)) { throw new InvalidOperationException("Live recovery failed."); }
            Console.WriteLine("Persistent conversation and reconnect passed.");
        }
    }
    if (args.Contains("--thinking-feedback", StringComparer.Ordinal))
    {
        await settings.UpdateAsync(settings.Current with { Culture = "pt-BR", Voice = settings.Current.Voice with { Volume = 0 } }, timeout.Token);
        await live.ReconfigureAsync();
        long feedbackBytes = 0; int feedbackMessages = 0;
        void CountFeedback(object? sender, byte[] pcm) => Interlocked.Add(ref feedbackBytes, pcm.Length);
        void CountMessage(object? sender, ConversationMessage message) => Interlocked.Increment(ref feedbackMessages);
        live.AudioOutputReceived += CountFeedback; live.MessageReceived += CountMessage;
        try
        {
            live.SetThinking(); await Task.Delay(300, timeout.Token); live.ReturnToReady();
            await Task.Delay(2500, timeout.Token);
            if (Interlocked.Read(ref feedbackBytes) != 0) { throw new InvalidOperationException("A quick response played stale thinking feedback."); }
            live.SetThinking();
            using var feedbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            feedbackTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (live.LastReadAloudTranscript != "Só um minuto." || live.IsSpeaking)
            { await Task.Delay(100, feedbackTimeout.Token); }
            long completed = Interlocked.Read(ref feedbackBytes);
            if (completed == 0 || !live.IsThinking || feedbackMessages != 0 || live.IsListening)
            { throw new InvalidOperationException("Thinking feedback failed audio, state, history or microphone verification."); }
            live.SetThinking(); await Task.Delay(2500, timeout.Token);
            if (Interlocked.Read(ref feedbackBytes) != completed) { throw new InvalidOperationException("Thinking feedback repeated in the same state."); }
            live.ReturnToReady();
            live.SetThinking();
            while (Interlocked.Read(ref feedbackBytes) == completed) { await Task.Delay(10, feedbackTimeout.Token); }
            live.ReturnToReady(); await Task.Delay(500, timeout.Token);
            long canceled = Interlocked.Read(ref feedbackBytes);
            if (live.IsSpeaking) { throw new InvalidOperationException("Thinking audio remained buffered after the response started."); }
            await Task.Delay(1500, timeout.Token);
            if (Interlocked.Read(ref feedbackBytes) != canceled) { throw new InvalidOperationException("Canceled thinking feedback kept producing audio."); }
            live.SetThinking(); await Task.Delay(300, timeout.Token); live.Interrupt(); live.ReturnToReady();
            await Task.Delay(2500, timeout.Token);
            if (Interlocked.Read(ref feedbackBytes) != canceled) { throw new InvalidOperationException("Interrupted thinking feedback still played."); }
            Console.WriteLine("Native Portuguese thinking voice, fast-response cancellation, one-shot delivery, unchanged thinking state, empty history and closed microphone verified.");
        }
        finally { live.ReturnToReady(); live.AudioOutputReceived -= CountFeedback; live.MessageReceived -= CountMessage; }
    }
    if (args.Contains("--long-audio-smoke", StringComparer.Ordinal))
    {
        await settings.UpdateAsync(settings.Current with { Culture = "pt-BR", Voice = settings.Current.Voice with { Volume = 0 } }, timeout.Token);
        await live.ReconfigureAsync();
        using var capture = new PcmAudioDiagnosticCapture();
        using var diagnostic = Laura.Core.Diagnostics.LauraDiagnostics.Capture();
        live.ConversationAudioReceived += capture.Receive; live.ConversationAudioPlayed += capture.Play;
        try
        {
            string prompt = "Leia um texto original de cerca de cento e vinte palavras, em português brasileiro, explicando como um jardim muda ao longo das estações. Fale com seu timbre natural constante, sem imitar personagens, sem cantar, sem ferramentas e sem mencionar este teste.";
            int promptFile = Array.IndexOf(args, "--prompt-file");
            if (promptFile >= 0)
            {
                if (promptFile + 1 >= args.Length || new FileInfo(args[promptFile + 1]).Length > 65536) { throw new InvalidOperationException("Provide an explicit local diagnostic prompt file of at most 64 KiB."); }
                prompt = await File.ReadAllTextAsync(args[promptFile + 1], timeout.Token);
            }
            string? answer = await live.AskAsync(new(prompt, CultureInfo.GetCultureInfo("pt-BR"), "Synthetic Tester"), timeout.Token);
            var comparison = capture.Compare();
            await capture.ExportAsync(directory, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "long-audio.json"), JsonSerializer.Serialize(new { comparison, Playback = live.PlaybackMetrics,
                DurationSeconds = comparison.ReceivedBytes / 48000d, Diagnostics = Laura.Core.Diagnostics.LauraDiagnostics.Snapshot() }), timeout.Token);
            if (string.IsNullOrWhiteSpace(answer) || comparison.ReceivedBytes == 0 || !comparison.Identical || live.PlaybackMetrics?.MisalignedReads != 0)
            { throw new InvalidOperationException("Long provider playback failed transcript, A/B PCM identity or sample alignment."); }
            Console.WriteLine("Long provider audio A/B bytes identical; native-rate WAVs and buffer counters exported. Subjective timbre and device output require separate listening.");
        }
        finally { live.ConversationAudioReceived -= capture.Receive; live.ConversationAudioPlayed -= capture.Play; }
    }
    if (args.Contains("--vad", StringComparer.Ordinal))
    {
        await settings.UpdateAsync(settings.Current with { Culture = "pt-BR" }, timeout.Token);
        await live.ReconfigureAsync();
        using var recording = new MemoryStream();
        void Capture(object? sender, byte[] pcm) => recording.Write(pcm);
        live.AudioOutputReceived += Capture;
        await live.SpeakAsync("Quanto é dois mais dois?", timeout.Token);
        live.AudioOutputReceived -= Capture;
        Console.WriteLine("Synthetic read-aloud transcript: " + live.LastReadAloudTranscript);
        byte[] source = recording.ToArray();
        if (source.Length == 0) { throw new InvalidOperationException("Synthetic speech returned no PCM."); }
        int count = source.Length / 2 * 2 / 3; byte[] input = new byte[count * 2];
        for (int sample = 0; sample < count; sample++)
        {
            int sourceSample = sample * 3 / 2;
            short value = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(sourceSample * 2));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(sample * 2), value);
        }
        string answer = await live.AskAudioAsync(input, timeout.Token);
        Console.WriteLine("Automatic input transcript: " + live.LastAudioInputTranscript);
        Console.WriteLine("Automatic answer: " + answer);
        if (!answer.Contains("quatro", StringComparison.OrdinalIgnoreCase) && !answer.Contains('4')) { throw new InvalidOperationException("Automatic audio turn did not answer the synthetic question: " + answer); }
        Console.WriteLine("Automatic VAD answered Brazilian Portuguese synthetic speech after silence, without an explicit stop signal.");
    }
    keys.Save("");
    Console.WriteLine("Selected reference voices passed; no microphone capture occurred.");
}
Console.WriteLine("Smoke verification completed.");
return 0;

internal sealed class SmokeShell : IShellController
{
    public void ShowSettings() { }
    public void ToggleSettings() { }
    public void Shutdown() { }
}

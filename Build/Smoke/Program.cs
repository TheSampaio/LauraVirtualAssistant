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
if (args.Contains("--silent-thinking", StringComparer.Ordinal))
{
    await using var live = new GeminiLiveClient(settings, new ProtectedApiKeyStore(directory), memory, Array.Empty<ISkill>(), new SmokeShell());
    int bytes = 0, messages = 0;
    live.AudioOutputReceived += (_, pcm) => Interlocked.Add(ref bytes, pcm.Length);
    live.MessageReceived += (_, _) => Interlocked.Increment(ref messages);
    live.SetThinking(); await Task.Delay(3100, timeout.Token);
    if (!live.IsThinking || live.IsSpeaking || live.IsListening || bytes != 0 || messages != 0)
    { throw new InvalidOperationException("Processing produced unsolicited speech, history or capture."); }
    live.ReturnToReady(); live.SetThinking(); live.Interrupt(); live.ReturnToReady();
    await Task.Delay(2200, timeout.Token);
    if (bytes != 0 || messages != 0) { throw new InvalidOperationException("Interrupted processing produced delayed output."); }
    Console.WriteLine("Silent native processing and interruption verified without credentials, cloud traffic or microphone capture.");
    return 0;
}
if (args.Contains("--gemini", StringComparer.Ordinal))
{
    int index = Array.IndexOf(args, "--key-file");
    if (index < 0 || index + 1 >= args.Length) { Console.Error.WriteLine("Provide the existing credential file with --key-file. No credential is printed."); return 2; }
    using JsonDocument config = JsonDocument.Parse(await File.ReadAllTextAsync(args[index + 1], timeout.Token));
    var keys = new ProtectedApiKeyStore(directory);
    keys.Save(config.RootElement.GetProperty("gemini_api_key").GetString() ?? "");
    await using var live = new GeminiLiveClient(settings, keys, memory, Array.Empty<ISkill>(), new SmokeShell());
    live.StatusChanged += (_, status) => Console.WriteLine("Live state: " + status);
    IReadOnlyList<string> selectedVoices = args.Contains("--session-reset-smoke", StringComparer.Ordinal) ? [] : args.Contains("--quick", StringComparer.Ordinal) ? ["Charon"] : GeminiLiveOptions.Voices;
    int selectedVoice = Array.IndexOf(args, "--voice");
    if (selectedVoice >= 0)
    {
        if (selectedVoice + 1 >= args.Length || !GeminiLiveOptions.Voices.Contains(args[selectedVoice + 1], StringComparer.Ordinal))
        { throw new InvalidOperationException("Select one of the configured native Gemini voices."); }
        selectedVoices = [args[selectedVoice + 1]];
    }
    if (args.Contains("--session-reset-smoke", StringComparer.Ordinal))
    {
        await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, RememberConversations = true, MicrophoneMuted = true,
            Voice = settings.Current.Voice with { Volume = 0 }, GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Provider = "gemini", Model = GeminiLiveOptions.Model } }, timeout.Token);
        await using var journal = new Laura.Core.Diagnostics.RuntimeJournal(Path.Combine(directory, "Diagnostics"));
        const string marker = "vulpine amethyst lighthouse";
        string? first = await live.AskAsync(new("For this synthetic dialogue, the exact three-word marker is '" + marker + "'. Say only those three words, without punctuation.", CultureInfo.GetCultureInfo("en-US"), "Synthetic Tester"), timeout.Token);
        if (first is null || !first.Contains("amethyst", StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Synthetic session marker was not established."); }
        memory.Append(new(ConversationMessageSource.UserTyped, "Synthetic prior marker: " + marker, DateTimeOffset.UtcNow));
        int updates = 0; var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Stream(object? sender, ConversationStreamUpdate update) { Interlocked.Increment(ref updates); if (!update.Completed && update.Text.Length > 0) { streamStarted.TrySetResult(); } }
        live.StreamUpdated += Stream;
        try
        {
            Task<string?> running = live.AskAsync(new("Describe a synthetic garden in two hundred words. No tools.", CultureInfo.GetCultureInfo("en-US"), "Synthetic Tester"), timeout.Token);
            await streamStarted.Task.WaitAsync(TimeSpan.FromSeconds(45), timeout.Token);
            bool deniedDuringReset = false;
            await live.ResetSessionAsync(async () =>
            {
                deniedDuringReset = !live.IsAvailable && await live.AskAsync(new("This must not open a session during reset.", CultureInfo.GetCultureInfo("en-US"), "Synthetic Tester"), timeout.Token) is null;
                memory.ClearHistory();
            });
            bool canceled = false; try { await running; } catch (OperationCanceledException) { canceled = true; }
            if (!canceled || !deniedDuringReset || memory.ReadAllHistory().Count != 0 || live.IsListening) { throw new InvalidOperationException("Streaming session did not reset safely."); }
            int settled = Volatile.Read(ref updates); await Task.Delay(300, timeout.Token);
            if (Volatile.Read(ref updates) != settled) { throw new InvalidOperationException("Old streaming survived session reset."); }
            string? clean = await live.AskAsync(new("What exact three-word marker did I give you earlier in this conversation? If this conversation has no earlier marker, say only EMPTY. Do not guess.", CultureInfo.GetCultureInfo("en-US"), "Synthetic Tester") { History = memory.ReadRecent(8) }, timeout.Token);
            if (clean is null || !clean.Contains("EMPTY", StringComparison.OrdinalIgnoreCase) || clean.Contains("amethyst", StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("A fresh provider session did not report empty prior context."); }
            var snapshot = await journal.SnapshotAsync(timeout.Token);
            await using (var output = File.Create(Path.Combine(directory, "diagnostic.zip"))) { await Laura.Core.Diagnostics.DiagnosticExport.WriteAsync(output, snapshot, "2.0.0", timeout.Token); }
            await File.WriteAllTextAsync(Path.Combine(directory, "session-reset.txt"), "Real Gemini established a synthetic marker, emitted visible streaming deltas, canceled an in-progress turn, refused a request during reset, cleared persisted history, emitted no late deltas and answered EMPTY in the fresh session. Volume zero; no microphone or private dialogue used.", timeout.Token);
            Console.WriteLine("Real streaming cancellation, clean provider session and technical diagnostic ZIP passed.");
        }
        finally { live.StreamUpdated -= Stream; }
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
            live.SetThinking(); await Task.Delay(3000, timeout.Token);
            if (!live.IsThinking || live.IsSpeaking || live.IsListening || feedbackMessages != 0 || Interlocked.Read(ref feedbackBytes) != 0)
            { throw new InvalidOperationException("Thinking state produced unsolicited speech, history or microphone capture."); }
            live.Interrupt(); live.ReturnToReady(); await Task.Delay(2500, timeout.Token);
            if (Interlocked.Read(ref feedbackBytes) != 0) { throw new InvalidOperationException("Interrupted processing produced unsolicited audio."); }
            Console.WriteLine("Silent thinking, fast and prolonged processing, interruption, empty history and closed microphone verified.");
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
    Console.WriteLine("Configured Live smoke checks passed; no microphone capture occurred.");
}
Console.WriteLine("Smoke verification completed.");
return 0;

internal sealed class SmokeShell : IShellController
{
    public void ShowSettings() { }
    public void ToggleSettings() { }
    public void Shutdown() { }
}

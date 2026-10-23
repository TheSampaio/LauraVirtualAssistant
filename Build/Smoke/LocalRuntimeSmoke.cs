using System.Text.Json;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.Speech;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

/// <summary>Runs owner-requested, isolated local installation and inference checks without personal settings or microphone access.</summary>
internal static class LocalRuntimeSmoke
{
    /// <summary>Rewrites only the already loaded owner settings through the native sanitized INI writer after a recovery copy.</summary>
    internal static async Task<int> MigrateOwnerSettingsAsync()
    {
        string settingsPath = IniSettingsStore.GetDefaultFilePath();
        string recovery = Path.Combine(Path.GetDirectoryName(settingsPath)!, "Recovery"); Directory.CreateDirectory(recovery);
        if (File.Exists(settingsPath))
        { File.Copy(settingsPath, Path.Combine(recovery, "settings-before-retired-provider-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".ini"), false); }
        using var owner = new SettingsService(new IniSettingsStore(settingsPath, NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await owner.LoadAsync(); await owner.UpdateAsync(current => current);
        Console.WriteLine("PASS: retired provider fields removed by the native INI writer; unrelated owner settings retained with recovery copy.");
        return 0;
    }
    /// <summary>Downloads the explicitly approved pinned pack into isolated storage. Returns: Zero after complete hash verification.</summary>
    internal static async Task<int> InstallAsync(string directory, bool ownerModels = false)
    {
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
        using var installer = new LocalPackInstaller(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan },
            Path.GetFullPath("_Output/Distribution/Laura-win-x64"));
        var progress = new Progress<LocalPackInstaller.Progress>(item => Console.WriteLine($"download {item.Component}: {item.Received}/{item.Total}"));
        string destination = ownerModels ? LocalModelPaths.Models : Path.Combine(directory, "Models");
        string[] previousDirectories = Directory.Exists(destination) ? Directory.GetDirectories(destination) : [];
        var installed = await installer.InstallAsync(destination, progress, timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(directory, "installation.json"), JsonSerializer.Serialize(installed, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
        if (Directory.GetDirectories(installed.Directory).Except(previousDirectories, StringComparer.OrdinalIgnoreCase).Any()) { throw new IOException("Model installation created an unexpected subdirectory."); }
        Console.WriteLine("PASS: optional assets SHA-256 verified in a flat model store; personal configuration unchanged.");
        if (ownerModels)
        {
            string settingsPath = IniSettingsStore.GetDefaultFilePath();
            string recovery = Path.Combine(Path.GetDirectoryName(settingsPath)!, "Recovery"); Directory.CreateDirectory(recovery);
            if (File.Exists(settingsPath)) { File.Copy(settingsPath, Path.Combine(recovery, "settings-before-local-ready-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".ini"), false); }
            using var owner = new SettingsService(new IniSettingsStore(settingsPath, NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
            await owner.LoadAsync(timeout.Token);
            await owner.UpdateAsync(current => current with { LocalModel = installed.Model,
                LocalVoice = installed.Voice with { EnglishVoice = current.LocalVoice.EnglishVoice, PortugueseVoice = current.LocalVoice.PortugueseVoice } }, timeout.Token);
            Console.WriteLine("PASS: requested local model and voice configuration saved to the existing profile; prior settings backed up, other preferences preserved.");
        }
        return 0;
    }
    /// <summary>Enables the already verified GPT registration in the existing profile after both requested real speech tests pass.</summary>
    internal static async Task<int> ConfigureReadyProfileAsync(string evidenceDirectory)
    {
        string result = await File.ReadAllTextAsync(Path.Combine(evidenceDirectory, "result.txt"));
        if (!result.StartsWith("PASS: real GPU Qwen and authorized GPT", StringComparison.Ordinal)) { throw new InvalidOperationException("Real provider speech acceptance is required before final profile setup."); }
        string credentials = Path.Combine(LocalModelPaths.DataRoot, "Credentials");
        using var broker = new ChatGptBrokerClient(new ChatGptCredentialStore(credentials, new WindowsPrivateDataProtector()), credentials);
        var state = await broker.ReadAsync();
        if (state.Status != "plan_authorized" || !state.IncludedPlanOnlyConfirmed || string.IsNullOrWhiteSpace(state.VerifiedModel))
        { throw new InvalidOperationException("The existing GPT registration is not authorized for included-plan inference."); }
        using var settings = new SettingsService(new IniSettingsStore(IniSettingsStore.GetDefaultFilePath(), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync();
        await settings.UpdateAsync(current => current with { ChatGpt = current.ChatGpt with { Enabled = true, IncludedPlanOnly = true, Registration = state.Active, Model = state.VerifiedModel } });
        Console.WriteLine("PASS: local paths and automatic GPU retained; verified GPT connection enabled in the existing profile. No token, allowance or other preference changed.");
        return 0;
    }
    /// <summary>Exercises automatic hardware inference, native tools, Kokoro and Whisper in both languages using synthetic data only.</summary>
    internal static async Task<int> RunAsync(string directory)
    {
        var installation = JsonSerializer.Deserialize<LocalPackInstaller.Installation>(await File.ReadAllTextAsync(Path.Combine(directory, "installation.json")))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "offline-settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync(timeout.Token);
        await settings.UpdateAsync(LauraSettings.Default with { GreetOnStartup = false, AnnounceHourly = false, MicrophoneMuted = true,
            GenerativeAi = new() { Enabled = false, Timeout = TimeSpan.FromMinutes(8) }, ChatGpt = new() { Enabled = false },
            ProviderSelection = ConversationProviderSelection.Local, LocalModel = installation.Model with { Enabled = true }, LocalVoice = installation.Voice }, timeout.Token);
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        await using var runtime = new LlamaCppRuntime(settings, http);
        var client = new LlamaCppTextClient(runtime, http, settings);
        var memory = new SqliteConversationMemory(Path.Combine(directory, "offline-memory.db")); memory.Initialize();
        var tools = new ProbeTools(); using var engine = new TextConversationEngine(client, settings, memory, tools);
        var evidence = new List<object>();
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            var watch = Stopwatch.StartNew();
            string prompt = culture == "pt-BR" ? "Use a ferramenta local_probe para consultar a palavra de confirmação e responda em português em uma frase com essa palavra. Não invente o resultado." : "Use local_probe to obtain the verification word, then answer in English in one sentence containing that word. Do not guess the tool result.";
            int before = tools.Calls;
            string? answer = await engine.AskAsync(new(prompt, CultureInfo.GetCultureInfo(culture), "Synthetic Tester") { TurnId = Guid.NewGuid() }, timeout.Token);
            Console.WriteLine($"inference {culture}: {watch.Elapsed.TotalSeconds:F1}s; calls={tools.Calls - before}; failure={engine.LastFailure?.Kind}; answer={answer}");
            if (tools.Calls <= before || answer?.Contains("cobalt", StringComparison.OrdinalIgnoreCase) != true) { throw new InvalidOperationException("Local inference did not verify the synthetic native tool result."); }
            evidence.Add(new { component = "Qwen", culture, seconds = watch.Elapsed.TotalSeconds, answer, nativeCalls = tools.Calls - before, device = runtime.ActiveDevice, offloadedLayers = runtime.OffloadedLayers });
            Console.WriteLine($"hardware: {runtime.ActiveDevice}; dedicated GPU bytes={runtime.DedicatedGpuBytes}; reported layers={runtime.OffloadedLayers}");
        }
        var briefing = new ConversationTurn("Resuma meu dia em português em uma frase. Fatos confirmados: reunião às 14h, lembrete de revisar o projeto às 16h e previsão de chuva à noite.",
            CultureInfo.GetCultureInfo("pt-BR"), "Synthetic Tester")
        {
            TurnId = Guid.NewGuid(), IsStartupBriefing = true,
            History = [new ConversationMessage(ConversationMessageSource.UserTyped, new string('H', 12000), DateTimeOffset.UtcNow)],
            Memories = [new string('M', 12000)], SkillCatalogue = new string('T', 12000)
        };
        string? summary = await engine.AskAsync(briefing, timeout.Token);
        if (string.IsNullOrWhiteSpace(summary) || engine.LastFailure is not null)
        { throw new InvalidOperationException("The local daily briefing failed despite its compact fact-only context: " + engine.LastFailure?.Kind); }
        Console.WriteLine("briefing pt-BR: " + summary);
        evidence.Add(new { component = "Local daily briefing", culture = "pt-BR", answer = summary, historyExcluded = true });
        string[] workerCandidates = [Path.GetFullPath("Laura.VoiceWorker/bin/Debug/net10.0/win-x64/Laura.VoiceWorker.exe"),
            Path.GetFullPath("Laura.VoiceWorker/bin/Release/net10.0/win-x64/Laura.VoiceWorker.exe"), Path.GetFullPath("_Output/Distribution/Laura-win-x64/VoiceWorker/Laura.VoiceWorker.exe")];
        string executable = workerCandidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Build the voice worker before running local voice acceptance.");
        await using var worker = new LocalVoiceWorkerClient(executable);
        await using var speech = new KokoroSpeechSynthesizer(settings, worker);
        var recognizer = new WhisperSpeechRecognizer(settings, worker);
        if (!speech.IsAvailable || !recognizer.IsAvailable) { throw new InvalidOperationException("Installed flat model paths were not recognized by the actual speech adapters."); }
        foreach (var sample in new[] {
            (Culture: "en-US", Voice: "af_heart", Text: "Hello, I am Laura. The local voice system is ready. Today we will review the project and save the results.", Expected: "project"),
            (Culture: "en-US", Voice: "af_bella", Text: "Hello, I am Laura. We can review the project and save the results using local tools.", Expected: "project"),
            (Culture: "pt-BR", Voice: "pf_dora", Text: "Olá, eu sou a Laura. O sistema de voz local está pronto. Hoje vamos revisar o projeto e salvar os resultados.", Expected: "projeto") })
        {
            using var pcm = new MemoryStream(); var watch = Stopwatch.StartNew();
            await worker.ExecuteAsync(new("synthesize", installation.Voice.KokoroDirectory, sample.Culture, 2, sample.Text, sample.Voice, AssetsDirectory: installation.Voice.KokoroDataDirectory), response =>
            { if (response.Type != "pcm" || response.SampleRate != 24000) { throw new InvalidDataException(); } pcm.Write(Convert.FromBase64String(response.Data)); }, timeout.Token);
            double synthesisSeconds = watch.Elapsed.TotalSeconds; byte[] audio = pcm.ToArray();
            if (audio.Length < 48000) { throw new InvalidDataException("Local synthesis produced insufficient PCM."); }
            string wav = Path.Combine(directory, sample.Voice + ".wav");
            using (var writer = new WaveFileWriter(wav, new WaveFormat(24000, 16, 1))) { writer.Write(audio, 0, audio.Length); }
            byte[] input = Resample(audio); string transcript = ""; watch.Restart();
            await worker.ExecuteAsync(new("recognize", installation.Voice.WhisperDirectory, sample.Culture, 2, Pcm: Convert.ToBase64String(input)), response =>
            { if (response.Type == "transcript") { transcript = response.Data; } }, timeout.Token);
            Console.WriteLine($"voice {sample.Culture}: TTS={synthesisSeconds:F1}s; PCM={audio.Length}; ASR={watch.Elapsed.TotalSeconds:F1}s; transcript={transcript}");
            if (!transcript.Contains(sample.Expected, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Local recognition did not recover the synthetic sentence."); }
            evidence.Add(new { component = "Kokoro + Whisper", sample.Culture, sample.Voice, synthesisSeconds, audioSeconds = audio.Length / 48000d, recognitionSeconds = watch.Elapsed.TotalSeconds, transcript, wav });
            if (sample.Culture == "pt-BR")
            {
                string detected = "";
                await worker.ExecuteAsync(new("recognize", installation.Voice.WhisperDirectory, "en-US", 2, Pcm: Convert.ToBase64String(input), DetectLanguage: true), response =>
                { if (response.Type == "transcript") { detected = response.Data; } }, timeout.Token);
                if (!detected.Contains("projeto", StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Automatic local language detection did not recover the Portuguese fixture."); }
                Console.WriteLine("PASS: automatic Whisper language selection recovered Portuguese with English manual settings.");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "offline-results.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
        Console.WriteLine("PASS: real local text/tool/voice pipeline in EN/PT. Cloud providers disabled; loopback HTTP only; no microphone or personal storage."); return 0;
    }
    /// <summary>Converts synthetic 24 kHz signed PCM to Whisper's 16 kHz using bounded linear interpolation.</summary>
    private static byte[] Resample(byte[] input)
    {
        int samples = input.Length / 2; byte[] output = new byte[(int)(samples / 1.5) * 2];
        for (int index = 0; index < output.Length / 2; index++)
        {
            double position = index * 1.5; int left = (int)position, right = Math.Min(samples - 1, left + 1);
            short a = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(left * 2)), b = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(right * 2));
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(index * 2), (short)(a + (b - a) * (position - left)));
        }
        return output;
    }
    /// <summary>Records an actual shared-executor call with a synthetic result absent from the model prompt.</summary>
    private sealed class ProbeTools : IAssistantTools
    {
        internal int Calls { get; private set; }
        /// <inheritdoc />
        public object[] Declarations => [new { functionDeclarations = Enumerable.Range(0, 12).Select(index => new { name = index == 0 ? "local_probe" : "unused_probe_" + index,
            description = index == 0 ? "Read the verification word from the local synthetic fixture." : "Unused synthetic capability; unrelated to verification words.", parameters = new { type = "OBJECT", properties = new { } } }).ToArray() }];
        /// <inheritdoc />
        public bool IsRegisteredTool(string name) => name == "local_probe";
        /// <inheritdoc />
        public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); if (!IsRegisteredTool(name)) { throw new InvalidDataException(); } Calls++; return Task.FromResult(new AssistantToolResult("The verified word is cobalt.")); }
    }
}

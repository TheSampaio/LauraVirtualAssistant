using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Laura.Core.Speech;
using SherpaOnnx;

namespace Laura.VoiceWorker;

/// <summary>Isolates local ONNX/phonemizer failures behind a bounded stdin/stdout protocol; it has no network or tool executor.</summary>
internal static class Program
{
    private static OfflineTts? _tts;
    private static OfflineRecognizer? _recognizer;
    private static string _ttsIdentity = "", _recognitionIdentity = "";
    /// <summary>Processes one request at a time and releases native models when the owning host closes stdin.</summary>
    private static async Task Main()
    {
        try
        {
            while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 1500000) { Emit(new("error", "request_bounds")); continue; }
                try
                {
                    var request = JsonSerializer.Deserialize<VoiceWorkerRequest>(line) ?? throw new InvalidDataException();
                    if (request.Threads is < 1 or > 8 || !Path.IsPathFullyQualified(request.Directory)) { throw new InvalidDataException(); }
                    if (request.Operation == "synthesize") { Synthesize(request); }
                    else if (request.Operation == "recognize") { Recognize(request); }
                    else { throw new InvalidDataException(); }
                    Emit(new("done"));
                }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or JsonException or DllNotFoundException or EntryPointNotFoundException)
                { Emit(new("error", "local_voice_failure")); }
            }
        }
        finally { _tts?.Dispose(); _recognizer?.Dispose(); }
    }
    /// <summary>Synthesizes a fixed reviewed Kokoro voice and emits bounded 24 kHz signed PCM fragments.</summary>
    private static void Synthesize(VoiceWorkerRequest request)
    {
        if (request.Text.Length is < 1 or > 16000 || request.Speed is < .5f or > 2f) { throw new InvalidDataException(); }
        bool piper = request.Voice is "pt_dii" or "gb_cori";
        string language = request.Culture == "pt-BR" ? "pt-br" : "en-us";
        string identity = request.Directory + ":" + request.AssetsDirectory + ":" + language + ":" + request.Threads + ":" + (piper ? request.Voice : "kokoro");
        if (_tts is null || _ttsIdentity != identity)
        {
            _tts?.Dispose(); _tts = null;
            var config = new OfflineTtsConfig();
            string dataDirectory = string.IsNullOrWhiteSpace(request.AssetsDirectory) ? Path.Combine(request.Directory, "espeak-ng-data") : request.AssetsDirectory;
            if (piper)
            {
                string prefix = request.Voice == "pt_dii" ? "piper-pt_BR-dii" : "piper-en_GB-cori";
                config.Model.Vits.Model = Path.Combine(request.Directory, prefix + ".onnx");
                config.Model.Vits.Tokens = Path.Combine(request.Directory, prefix + "-tokens.txt");
                config.Model.Vits.DataDir = dataDirectory;
            }
            else
            {
                string prefix = File.Exists(Path.Combine(request.Directory, "kokoro-model.onnx")) ? "kokoro-" : "";
                config.Model.Kokoro.Model = Path.Combine(request.Directory, prefix + "model.onnx"); config.Model.Kokoro.Voices = Path.Combine(request.Directory, prefix + "voices.bin");
                config.Model.Kokoro.Tokens = Path.Combine(request.Directory, prefix + "tokens.txt"); config.Model.Kokoro.DataDir = dataDirectory;
                config.Model.Kokoro.Lang = language;
            }
            config.Model.NumThreads = request.Threads; config.Model.Provider = "cpu"; config.Model.Debug = 0;
            config.MaxNumSentences = 1;
            _tts = new OfflineTts(config); _ttsIdentity = identity;
        }
        int speaker = 0;
        if (!piper)
        {
            var voices = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(request.Directory, File.Exists(Path.Combine(request.Directory, "kokoro-voices.json")) ? "kokoro-voices.json" : "voices.json")));
            if (voices is null || !voices.TryGetValue(request.Voice, out speaker)) { throw new InvalidDataException(); }
        }
        if (speaker < 0 || speaker >= _tts.NumSpeakers || _tts.SampleRate is not (22050 or 24000)) { throw new InvalidDataException(); }
        var callback = new OfflineTtsCallbackProgress((samples, count, _) =>
        {
            float[] source = new float[count]; Marshal.Copy(samples, source, 0, count);
            float[] data = _tts.SampleRate == 24000 ? source : Resample(source, _tts.SampleRate, 24000);
            for (int offset = 0; offset < data.Length; offset += 2048)
            {
                int size = Math.Min(2048, data.Length - offset); byte[] pcm = new byte[size * 2];
                for (int index = 0; index < size; index++) { BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2), (short)Math.Clamp(data[offset + index] * 32767f, short.MinValue, short.MaxValue)); }
                Emit(new("pcm", Convert.ToBase64String(pcm), 24000));
            }
            return 1;
        });
        var audio = _tts.GenerateWithCallbackProgress(request.Text, request.Speed, speaker, callback); audio.Dispose();
    }
    /// <summary>Converts a Piper fragment to the fixed 24 kHz playback protocol with bounded linear interpolation.</summary>
    private static float[] Resample(float[] source, int from, int to)
    {
        var target = new float[(int)Math.Round(source.Length * (double)to / from)];
        for (int index = 0; index < target.Length; index++)
        {
            double position = index * (double)from / to;
            int left = Math.Min((int)position, source.Length - 1), right = Math.Min(left + 1, source.Length - 1);
            target[index] = source[left] + (source[right] - source[left]) * (float)(position - left);
        }
        return target;
    }
    /// <summary>Decodes one bounded multilingual Whisper utterance locally without keeping its PCM after completion.</summary>
    private static void Recognize(VoiceWorkerRequest request)
    {
        byte[] pcm = Convert.FromBase64String(request.Pcm);
        if (pcm.Length is < 3200 or > 960000 || (pcm.Length & 1) != 0) { throw new InvalidDataException(); }
        string language = request.DetectLanguage ? "" : request.Culture == "pt-BR" ? "pt" : "en";
        string identity = request.Directory + ":" + language + ":" + request.Threads;
        if (_recognizer is null || _recognitionIdentity != identity)
        {
            _recognizer?.Dispose(); _recognizer = null;
            var config = new OfflineRecognizerConfig(); config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = 80;
            // Both multilingual Whisper checkpoints use the same vocabulary; retain the verified base token artifact.
            config.ModelConfig.Tokens = Find(request.Directory, "base-tokens.txt", "*-tokens.txt"); config.ModelConfig.NumThreads = request.Threads;
            config.ModelConfig.Provider = "cpu"; config.ModelConfig.Debug = 0;
            bool small = File.Exists(Path.Combine(request.Directory, "small-encoder.int8.onnx"))
                && File.Exists(Path.Combine(request.Directory, "small-decoder.int8.onnx"));
            config.ModelConfig.Whisper.Encoder = Find(request.Directory, small ? "small-encoder.int8.onnx" : "base-encoder.int8.onnx", "*-encoder.onnx");
            config.ModelConfig.Whisper.Decoder = Find(request.Directory, small ? "small-decoder.int8.onnx" : "base-decoder.int8.onnx", "*-decoder.onnx");
            config.ModelConfig.Whisper.Language = language; config.ModelConfig.Whisper.Task = "transcribe";
            _recognizer = new OfflineRecognizer(config); _recognitionIdentity = identity;
        }
        float[] samples = new float[pcm.Length / 2];
        for (int index = 0; index < samples.Length; index++) { samples[index] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(index * 2)) / 32768f; }
        using var stream = _recognizer.CreateStream(); stream.AcceptWaveform(16000, samples); _recognizer.Decode(stream);
        string text = stream.Result.Text.Trim();
        Emit(new("transcript", text is "." or "<noise>" or "[BLANK_AUDIO]" || text.Length > 4000 ? "" : text));
    }
    /// <summary>Resolves a unique known artifact filename inside the selected model directory.</summary>
    private static string Find(string directory, params string[] patterns)
    {
        foreach (string pattern in patterns) { string[] files = Directory.GetFiles(directory, pattern); if (files.Length == 1) { return files[0]; } }
        throw new FileNotFoundException("A required local voice artifact is unavailable.");
    }
    /// <summary>Writes private protocol data only to the parent pipe; native diagnostics use stderr and are discarded by the host.</summary>
    private static void Emit(VoiceWorkerEvent message) { Console.Out.WriteLine(JsonSerializer.Serialize(message)); Console.Out.Flush(); }
}

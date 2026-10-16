using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Models;
using Laura.Platform.Windows.Speech;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NativeSpeech = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

/// <summary>Uses only synthetic speech streams and never opens a microphone or playback device.</summary>
internal static class OfflineVoiceSmoke
{
    /// <summary>Verifies pinned Whisper Base, silence suppression and a Windows-synthesized local speech waveform without cloud credentials.</summary>
    internal static async Task RunAsync(string directory, bool download, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var models = new LocalModelManager(Path.Combine(directory, "Models"), http, SupportedModels.All);
        if (!File.Exists(models.GetModelPath(SupportedModels.Speech.Id)))
        { if (!download) { throw new FileNotFoundException("Install Whisper Base or pass --download."); } await models.InstallAsync(SupportedModels.Speech, null, cancellationToken); }
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync(cancellationToken); await using var whisper = new WhisperSpeechInput(models, settings);
        if (await whisper.TranscribeAsync(new float[16000], "pt-BR", cancellationToken) is not null) { throw new InvalidOperationException("Silence generated an accidental local command."); }
        var voice = NativeSpeech.AllVoices.FirstOrDefault(voice => voice.Language == "pt-BR") ?? NativeSpeech.AllVoices.FirstOrDefault(voice => voice.Language == "en-US")
            ?? throw new InvalidOperationException("No installed Windows EN/PT speech voice is available for the synthetic test.");
        using var speech = new NativeSpeech { Voice = voice };
        using var audio = await speech.SynthesizeTextToStreamAsync(voice.Language == "pt-BR" ? "A resposta é quatro." : "The answer is four.").AsTask(cancellationToken);
        using var stream = audio.AsStreamForRead(); using var wave = new WaveFileReader(stream);
        ISampleProvider source = wave.ToSampleProvider();
        if (source.WaveFormat.Channels == 2) { source = new StereoToMonoSampleProvider(source); }
        var resampled = new WdlResamplingSampleProvider(source, 16000);
        float[] samples = new float[240_000]; int count = resampled.Read(samples, 0, samples.Length);
        string? transcript = await whisper.TranscribeAsync(samples.AsMemory(0, count), voice.Language, cancellationToken);
        if (string.IsNullOrWhiteSpace(transcript) || !(transcript.Contains("four", StringComparison.OrdinalIgnoreCase) || transcript.Contains("quatro", StringComparison.OrdinalIgnoreCase) || transcript.Contains('4')))
        { throw new InvalidOperationException("Synthetic offline speech was not recognized as the expected fixture; samples: " + count + "; recognized characters: " + (transcript?.Length ?? 0) + "."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "offline-voice-result.json"), JsonSerializer.Serialize(new
        { voice.Language, waveformSampleRate = wave.WaveFormat.SampleRate, recognitionSampleRate = 16000, syntheticSamples = count, transcriptCharacters = transcript.Length,
            silenceSuppressed = true, microphoneOpened = false, playbackOpened = false, cloudUsed = false }), cancellationToken);
        Console.WriteLine("Offline Windows synthesis stream, pinned Whisper Base recognition, silence rejection and sample-rate conversion verified without microphone, speaker or cloud use.");
    }
}

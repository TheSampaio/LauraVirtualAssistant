using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Models;
using Laura.Core.Speech;
using NAudio.Wave;
using Whisper.net;

namespace Laura.Platform.Windows.Speech;

/// <summary>Captures only on explicit request and transcribes bounded speech locally with the pinned multilingual Whisper Base.</summary>
public sealed class WhisperSpeechInput(ILocalModelManager models, ISettingsService settings) : ISpeechInput, IAsyncDisposable
{
    private readonly SemaphoreSlim _recognition = new(1, 1);
    private WhisperFactory? _factory;
    /// <inheritdoc />
    public IReadOnlyList<string> Languages => ["en-US", "pt-BR"];
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> InputDevices => Enumerable.Range(0, WaveInEvent.DeviceCount).Select(index => new AudioDevice(index, WaveInEvent.GetCapabilities(index).ProductName)).ToArray();
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> OutputDevices => Enumerable.Range(0, WaveInterop.waveOutGetNumDevs()).Select(index =>
        WaveInterop.waveOutGetDevCaps((nint)index, out WaveOutCapabilities capabilities, Marshal.SizeOf<WaveOutCapabilities>()) == NAudio.MmResult.NoError
            ? new AudioDevice(index, capabilities.ProductName) : new AudioDevice(index, "unavailable")).ToArray();

    /// <inheritdoc />
    public Task<string?> RecognizeAsync(string culture, int device, CancellationToken stopRecording, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        if (settings.Current.MicrophoneMuted || stopRecording.IsCancellationRequested) { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(models.GetModelPath(SupportedModels.Speech.Id))) { throw new FileNotFoundException("Install multilingual Whisper Base before opening local speech capture."); }
        var detector = new SpeechTurnDetector(Math.Max(1000, settings.Current.GenerativeAi.SilenceDurationMs));
        using var audio = new MemoryStream(); using var capture = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 20 };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, data) =>
        {
            if (audio.Length + data.BytesRecorded > 480_000 || settings.Current.MicrophoneMuted) { finished.TrySetResult(); return; }
            audio.Write(data.Buffer, 0, data.BytesRecorded);
            if (detector.Observe(data.Buffer.AsSpan(0, data.BytesRecorded))) { finished.TrySetResult(); }
        };
        capture.RecordingStopped += (_, result) =>
        { if (result.Exception is { } error) { finished.TrySetException(error); } stopped.TrySetResult(); };
        if (settings.Current.MicrophoneMuted) { return null; }
        capture.StartRecording();
        using var release = stopRecording.Register(() => finished.TrySetResult());
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { }
        finally { capture.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!detector.HasSpeech || settings.Current.MicrophoneMuted) { return null; }
        byte[] pcm = audio.ToArray(); float[] samples = new float[pcm.Length / 2];
        for (int index = 0; index < samples.Length; index++) { samples[index] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(index * 2)) / 32768f; }
        return await TranscribeAsync(samples, culture, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    /// <summary>Transcribes only finite bounded non-silent 16 kHz samples. Args: samples: Local audio; culture: Configured EN/PT language; cancellationToken: Stop signal. Returns: Recognized text or null for silence.</summary>
    public async Task<string?> TranscribeAsync(ReadOnlyMemory<float> samples, string culture, CancellationToken cancellationToken)
    {
        if (samples.Length is < 2560 or > 240_000 || samples.Span.ContainsAnyExceptInRange(-1f, 1f)) { return null; }
        double energy = 0; foreach (float sample in samples.Span) { if (!float.IsFinite(sample)) { return null; } energy += sample * sample; }
        if (Math.Sqrt(energy / samples.Length) < .008) { return null; }
        await _recognition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = models.GetModelPath(SupportedModels.Speech.Id);
            if (_factory is null)
            {
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                if (file.Length != SupportedModels.Speech.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).Equals(SupportedModels.Speech.Sha256, StringComparison.OrdinalIgnoreCase))
                { throw new IOException("Whisper Base integrity verification failed."); }
                _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
            }
            await using var processor = _factory.CreateBuilder().WithLanguage(culture == "pt-BR" ? "pt" : "en").WithNoContext().WithNoSpeechThreshold(.7f).WithLogProbThreshold(-.5f).Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            { if (text.Length + segment.Text.Length > 4000) { throw new InvalidOperationException("Recognized speech exceeds the local command limit."); } text.Append(segment.Text); }
            cancellationToken.ThrowIfCancellationRequested(); return string.IsNullOrWhiteSpace(text.ToString()) ? null : text.ToString().Trim();
        }
        finally { _recognition.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    { await _recognition.WaitAsync().ConfigureAwait(false); try { _factory?.Dispose(); _factory = null; } finally { _recognition.Release(); } }
}

using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SherpaOnnx;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Optional local neural TTS backed by sherpa-onnx.
/// </summary>
public sealed class SherpaOnnxSpeechSynthesizer : ISpeechSynthesizer
{
    private const string VoiceId = "laura-natural-sherpa-kokoro";
    private const double RatePerStep = 0.08;

    private readonly WinRtSpeechSynthesizer _fallback;
    private readonly string _modelDirectory;
    private readonly ILogger<SherpaOnnxSpeechSynthesizer> _logger;
    private readonly SemaphoreSlim _speechGate = new(1, 1);

    private OfflineTts? _tts;
    private WaveOutEvent? _output;
    private volatile bool _speaking;
    private bool _disposed;

    /// <summary>
    /// Initializes the optional local neural synthesizer.
    /// </summary>
    /// <param name="fallback">Windows synthesizer used when no Sherpa model is installed.</param>
    /// <param name="modelDirectory">Directory containing the Kokoro model files.</param>
    /// <param name="logger">Destination for diagnostic logs.</param>
    public SherpaOnnxSpeechSynthesizer(
        WinRtSpeechSynthesizer fallback,
        string modelDirectory,
        ILogger<SherpaOnnxSpeechSynthesizer> logger)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _fallback = fallback;
        _modelDirectory = modelDirectory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSpeaking => _speaking || _fallback.IsSpeaking;

    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices()
    {
        IReadOnlyList<VoiceDescriptor> fallbackVoices = _fallback.GetAvailableVoices();

        return IsModelAvailable()
            ?
            [
                new VoiceDescriptor(VoiceId, "Laura Natural (Sherpa/Kokoro) - English (United States)", "en-US", true),
                .. fallbackVoices,
            ]
            : fallbackVoices;
    }

    /// <inheritdoc />
    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ShouldUseSherpa(request))
        {
            await _fallback.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            OfflineTts tts = GetOrCreateTts();
            var config = new OfflineTtsGenerationConfig
            {
                Sid = 0,
                Speed = (float)Math.Clamp(1.0 + request.Voice.Rate * RatePerStep, 0.6, 1.8),
                SilenceScale = 0.2f,
            };

            OfflineTtsGeneratedAudio audio = tts.GenerateWithConfig(request.Text, config, null);

            string tempPath = Path.Combine(Path.GetTempPath(), $"laura-tts-{Guid.NewGuid():N}.wav");

            try
            {
                if (!audio.SaveToWaveFile(tempPath))
                {
                    await _fallback.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await PlayAsync(tempPath, request.Voice, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or DllNotFoundException)
        {
            _logger.LogWarning(exception, "Sherpa natural TTS failed; falling back to Windows speech.");
            await _fallback.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _speechGate.Release();
        }
    }

    /// <inheritdoc />
    public void CancelSpeech()
    {
        _output?.Stop();
        _fallback.CancelSpeech();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancelSpeech();
        _output?.Dispose();
        _tts?.Dispose();
        _speechGate.Dispose();
        await _fallback.DisposeAsync().ConfigureAwait(false);
    }

    private bool ShouldUseSherpa(SpeechRequest request) =>
        IsModelAvailable()
        && (request.Voice.VoiceName is null
            || string.Equals(request.Voice.VoiceName, VoiceId, StringComparison.Ordinal));

    private bool IsModelAvailable() =>
        File.Exists(Path.Combine(_modelDirectory, "model.onnx"))
        && File.Exists(Path.Combine(_modelDirectory, "voices.bin"))
        && File.Exists(Path.Combine(_modelDirectory, "tokens.txt"))
        && Directory.Exists(Path.Combine(_modelDirectory, "espeak-ng-data"));

    private OfflineTts GetOrCreateTts()
    {
        if (_tts is not null)
        {
            return _tts;
        }

        var config = new OfflineTtsConfig();
        config.Model.Kokoro.Model = Path.Combine(_modelDirectory, "model.onnx");
        config.Model.Kokoro.Voices = Path.Combine(_modelDirectory, "voices.bin");
        config.Model.Kokoro.Tokens = Path.Combine(_modelDirectory, "tokens.txt");
        config.Model.Kokoro.DataDir = Path.Combine(_modelDirectory, "espeak-ng-data");

        string lexicon = Path.Combine(_modelDirectory, "lexicon-us-en.txt");

        if (File.Exists(lexicon))
        {
            config.Model.Kokoro.Lexicon = lexicon;
        }

        config.Model.NumThreads = 2;
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 1;

        _tts = new OfflineTts(config);
        return _tts;
    }

    private async Task PlayAsync(string path, VoiceProfile profile, CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(path)
        {
            Volume = (float)Math.Clamp(profile.Volume / 100.0, 0.0, 1.0),
        };
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, _) => completion.TrySetResult();
        output.Init(reader);

        _speaking = true;
        _output = output;

        await using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            output.Stop();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            output.Play();
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _speaking = false;
            _output = null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

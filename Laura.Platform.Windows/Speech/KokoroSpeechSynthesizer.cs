using Laura.Core.Abstractions;
using Laura.Core.Diagnostics;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;

namespace Laura.Platform.Windows.Speech;

/// <summary>Synthesizes every provider's prose with isolated local Kokoro inference and native PCM playback.</summary>
public sealed class KokoroSpeechSynthesizer(ISettingsService settings, LocalVoiceWorkerClient worker) : ISpeechSynthesizer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _active;
    private LiveAudioPlayer? _player;
    private static readonly string[] RequiredAssets = ["model.onnx", "voices.bin", "tokens.txt", "voices.json"];
    /// <summary>Gets whether the reviewed local synthesis assets and worker are installed.</summary>
    public bool IsAvailable => worker.IsInstalled && RequiredAssets.All(name => File.Exists(Path.Combine(settings.Current.LocalVoice.KokoroDirectory, name)))
        || worker.IsInstalled && RequiredAssets.All(name => File.Exists(Path.Combine(settings.Current.LocalVoice.KokoroDirectory, "kokoro-" + name)));
    /// <inheritdoc />
    public bool IsSpeaking => _active is not null;
    /// <summary>Gets actual output queue activity, independently of inference.</summary>
    public bool IsPlaying => _player?.IsPlaying == true;
    /// <summary>Gets actual PCM amplitude for the HUD.</summary>
    public float OutputLevel => _player?.Level ?? 0;
    /// <inheritdoc />
    public SpeechPlayback? LastPlayback { get; private set; }
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices()
    {
        if (!IsAvailable) { return []; }
        var voices = new List<VoiceDescriptor>
        {
            new("pf_dora", "Kokoro · Dora", "pt-BR", true), new("af_heart", "Kokoro · Heart", "en-US", true),
            new("af_bella", "Kokoro · Bella", "en-US", true), new("bf_alice", "Kokoro · Alice", "en-GB", true),
            new("bf_emma", "Kokoro · Emma", "en-GB", true), new("bf_isabella", "Kokoro · Isabella", "en-GB", true),
            new("bf_lily", "Kokoro · Lily", "en-GB", true)
        };
        if (HasPiper("piper-pt_BR-dii")) { voices.Add(new("pt_dii", "Dii · TigreGotico Lda · non-commercial", "pt-BR", true)); }
        if (HasPiper("piper-en_GB-cori")) { voices.Add(new("gb_cori", "Piper · Cori", "en-GB", true)); }
        return voices;
    }
    /// <inheritdoc />
    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _active = operation;
        try
        {
            if (!IsAvailable) { throw new SpeechUnavailableException("Local Kokoro assets are not installed; the response remains available as text."); }
            using var player = new LiveAudioPlayer(request.OutputDevice, request.Voice.Volume); _player = player; player.BeginTurn();
            string voice = request.Culture == "pt-BR" ? settings.Current.LocalVoice.PortugueseVoice : settings.Current.LocalVoice.EnglishVoice;
            if (voice == "pt_dii" && !HasPiper("piper-pt_BR-dii")) { voice = "pf_dora"; }
            if (voice == "gb_cori" && !HasPiper("piper-en_GB-cori")) { voice = "bf_emma"; }
            LastPlayback = new(request.ResponseId, player.PlaybackId, SpeechPlaybackState.Synthesizing, OutputDevice: request.OutputDevice);
            foreach (string segment in SpokenTextFormatter.Segments(request.Text))
            {
            player.BeginTurn();
            await worker.ExecuteAsync(new("synthesize", settings.Current.LocalVoice.KokoroDirectory, request.Culture, settings.Current.LocalVoice.Threads,
                segment, voice, Math.Clamp(1f + request.Voice.Rate * .04f, .6f, 1.4f), AssetsDirectory: settings.Current.LocalVoice.KokoroDataDirectory), response =>
                {
                    if (response.Type != "pcm" || response.SampleRate != 24000) { throw new InvalidDataException("Unexpected local synthesis format."); }
                    player.Add(Convert.FromBase64String(response.Data));
                }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested(); player.CompleteTurn(); await player.DrainAsync(operation.Token).ConfigureAwait(false);
            }
            LastPlayback = new(request.ResponseId, player.PlaybackId, SpeechPlaybackState.Completed, player.BytesReceived, player.BytesPlayed, player.DevicePositionBytes,
                request.OutputDevice, player.FirstAudioMilliseconds, player.Metrics.Underruns);
            if (player.FirstAudioMilliseconds is { } first) { LauraDiagnostics.Record("speech.first_audio", first, outcome: "kokoro"); }
        }
        catch (OperationCanceledException) { LastPlayback = LastPlayback is { } playback ? playback with { State = SpeechPlaybackState.Canceled } : null; throw; }
        catch (SpeechUnavailableException) { throw; }
        catch (Exception error) when (error is InvalidOperationException or IOException or NAudio.MmException)
        { throw new SpeechPlaybackException("Local Kokoro synthesis or playback failed; the response remains available as text.", error); }
        finally { _player = null; _active = null; _gate.Release(); }
    }
    /// <inheritdoc />
    public void CancelSpeech() { try { _active?.Cancel(); } catch (ObjectDisposedException) { } _player?.Clear(); }
    /// <summary>Checks both flat Piper artifacts before exposing or selecting a voice.</summary>
    private bool HasPiper(string prefix) => File.Exists(Path.Combine(settings.Current.LocalVoice.KokoroDirectory, prefix + ".onnx"))
        && File.Exists(Path.Combine(settings.Current.LocalVoice.KokoroDirectory, prefix + "-tokens.txt"));
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { CancelSpeech(); await _gate.WaitAsync().ConfigureAwait(false); _gate.Release(); GC.SuppressFinalize(this); }
}

using Laura.Core.Abstractions;
using Laura.Core.Speech;
using NAudio.Wave;
using NativeSpeech = Windows.Media.SpeechSynthesis.SpeechSynthesizer;
using VoiceGender = Windows.Media.SpeechSynthesis.VoiceGender;

namespace Laura.Platform.Windows.Speech;

/// <summary>Uses an already installed Windows voice locally; no voice assets, downloads or cloud fallback are bundled.</summary>
public sealed class WindowsOfflineSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly SemaphoreSlim _speech = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private int _speaking;
    /// <inheritdoc />
    public bool IsSpeaking => Volatile.Read(ref _speaking) != 0;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => NativeSpeech.AllVoices.Select(voice =>
        new VoiceDescriptor(voice.Id, voice.DisplayName, voice.Language, voice.Gender == VoiceGender.Female)).ToArray();

    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (request.Voice.Volume == 0 || string.IsNullOrWhiteSpace(request.Text)) { return; }
        await _speech.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            lock (_gate) { _active = operation; }
            using var synthesis = new NativeSpeech();
            synthesis.Voice = NativeSpeech.AllVoices.Where(voice => voice.Language.Equals(request.Culture, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(voice => voice.Gender == VoiceGender.Female).FirstOrDefault()
                ?? throw new InvalidOperationException("Install a Windows speech voice for the configured conversation language before using offline speech.");
            synthesis.Options.SpeakingRate = 1 + request.Voice.Rate * .05;
            synthesis.Options.AudioPitch = 1 + request.Voice.Pitch * .03;
            using var audio = await synthesis.SynthesizeTextToStreamAsync(request.Text).AsTask(operation.Token).ConfigureAwait(false);
            using var stream = audio.AsStreamForRead(); using var wave = new WaveFileReader(stream);
            using var player = new WaveOutEvent { DeviceNumber = request.OutputDevice, Volume = request.Voice.Volume / 100f, DesiredLatency = 120 };
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackStopped += (_, result) => { if (result.Exception is { } error) { completed.TrySetException(error); } else { completed.TrySetResult(); } };
            player.Init(wave); Volatile.Write(ref _speaking, 1);
            operation.Token.ThrowIfCancellationRequested(); player.Play();
            await completed.Task.WaitAsync(operation.Token).ConfigureAwait(false); operation.Token.ThrowIfCancellationRequested();
        }
        finally { Volatile.Write(ref _speaking, 0); lock (_gate) { if (ReferenceEquals(_active, operation)) { _active = null; } } _speech.Release(); }
    }, cancellationToken);

    /// <inheritdoc />
    public void CancelSpeech() { lock (_gate) { _active?.Cancel(); } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { CancelSpeech(); await _speech.WaitAsync().ConfigureAwait(false); _speech.Release(); }
}

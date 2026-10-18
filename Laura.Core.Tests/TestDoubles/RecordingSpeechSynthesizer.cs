using Laura.Core.Abstractions;
using Laura.Core.Speech;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records completed speech requests without opening a device or network connection.</summary>
public sealed class RecordingSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>Gets text accepted for speech.</summary>
    public List<string> SpokenTexts { get; } = [];
    /// <summary>Gets whether synthetic voice is available independently of generated text.</summary>
    public bool Available { get; init; } = true;
    /// <summary>Gets an optional component failure without affecting the dispatcher or text provider.</summary>
    public bool PlaybackFails { get; init; }
    /// <inheritdoc />
    public bool IsSpeaking => false;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => [];
    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Available) { throw new SpeechUnavailableException("Synthetic voice unavailable."); }
        if (PlaybackFails) { throw new SpeechPlaybackException("Synthetic output device failure."); }
        SpokenTexts.Add(request.Text); return Task.CompletedTask;
    }
    /// <inheritdoc />
    public void CancelSpeech() { }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

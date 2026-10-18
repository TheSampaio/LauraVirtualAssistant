using Laura.Core.Abstractions;
using Laura.Core.Speech;
using Laura.Core.Conversation;
using Laura.Platform.Windows.Conversation;

namespace Laura.Platform.Windows.Speech;

/// <summary>Uses the separately authorized Gemini Live voice for text from either reasoning provider.</summary>
public sealed class SpeechRouter(GeminiLiveClient live) : ISpeechSynthesizer
{
    /// <inheritdoc />
    public bool IsSpeaking => live.IsSpeaking;
    /// <inheritdoc />
    public SpeechPlayback? LastPlayback => live.LastSpeechPlayback;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => [];
    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default) =>
        live.SpeakAsync(request.Text, cancellationToken, request.ResponseId);
    /// <inheritdoc />
    public void CancelSpeech() => live.Interrupt();
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

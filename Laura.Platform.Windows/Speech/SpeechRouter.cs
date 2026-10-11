using Laura.Core.Abstractions;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;

namespace Laura.Platform.Windows.Speech;

/// <summary>Uses Gemini's native Live voice when selected, otherwise preserves offline speech.</summary>
public sealed class SpeechRouter(GeminiLiveClient live) : ISpeechSynthesizer
{
    /// <inheritdoc />
    public bool IsSpeaking => live.IsSpeaking;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => [];
    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default) =>
        live.IsAvailable ? live.SpeakAsync(request.Text, cancellationToken) : Task.CompletedTask;
    /// <inheritdoc />
    public void CancelSpeech() => live.Interrupt();
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

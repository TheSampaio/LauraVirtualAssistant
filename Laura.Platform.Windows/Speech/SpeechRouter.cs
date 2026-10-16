using Laura.Core.Abstractions;
using Laura.Core.Speech;
using Laura.Core.Conversation;
using Laura.Platform.Windows.Conversation;

namespace Laura.Platform.Windows.Speech;

/// <summary>Uses Gemini's native Live voice when selected, otherwise preserves offline speech.</summary>
public sealed class SpeechRouter(GeminiLiveClient live, WindowsOfflineSpeechSynthesizer offline, ConversationProviderRouter router) : ISpeechSynthesizer
{
    /// <inheritdoc />
    public bool IsSpeaking => live.IsSpeaking || offline.IsSpeaking;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => offline.GetAvailableVoices();
    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default) =>
        router.ActiveProvider == "gemini" && live.IsAvailable ? live.SpeakAsync(request.Text, cancellationToken)
        : router.ActiveProvider == "local" ? offline.SpeakAsync(request, cancellationToken) : Task.CompletedTask;
    /// <inheritdoc />
    public void CancelSpeech() { live.Interrupt(); offline.CancelSpeech(); }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;

namespace Laura.Platform.Windows.Speech;

/// <summary>Speaks Gemini turns with native Live audio and local or ChatGPT turns with the installed female local voice.</summary>
public sealed class ProviderSpeechRouter(ISettingsService settings, ConversationProviderRouter router,
    GeminiLiveClient gemini, KokoroSpeechSynthesizer local) : ISpeechSynthesizer
{
    private bool UseGemini => settings.Current.ProviderSelection == ConversationProviderSelection.Gemini
        || settings.Current.ProviderSelection == ConversationProviderSelection.Automatic
            && router.ActiveProvider == "gemini";

    /// <inheritdoc />
    public bool IsSpeaking => gemini.IsSpeaking || local.IsSpeaking;
    /// <inheritdoc />
    public SpeechPlayback? LastPlayback => UseGemini ? gemini.LastSpeechPlayback : local.LastPlayback;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => UseGemini
        ? gemini.IsAvailable ? [new(settings.Current.GenerativeAi.LiveVoice, "Gemini Live", settings.Current.Culture, true)] : []
        : local.GetAvailableVoices();
    /// <inheritdoc />
    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        if (UseGemini) { await gemini.SpeakAsync(request.Text, cancellationToken, request.ResponseId).ConfigureAwait(false); }
        else { await local.SpeakAsync(request, cancellationToken).ConfigureAwait(false); }
    }
    /// <inheritdoc />
    public void CancelSpeech() { gemini.Interrupt(); local.CancelSpeech(); }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

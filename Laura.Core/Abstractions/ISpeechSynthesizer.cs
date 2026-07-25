using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>
/// Speech synthesis engine (text-to-speech).
///
/// Abstracts the concrete engine - SAPI on Windows today - so the domain does not depend
/// on any platform API and can be exercised with test doubles.
/// </summary>
public interface ISpeechSynthesizer : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether speech is currently in progress.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Lists the voices installed on the system.
    ///
    /// Returns:
    ///     The available voices, possibly empty when no
    ///     synthesis engine is installed.
    /// </summary>
    IReadOnlyList<VoiceDescriptor> GetAvailableVoices();

    /// <summary>
    /// Speaks the request text and waits for the utterance to finish.
    ///
    /// Concurrent calls are serialized by the implementation: the second speech
    /// starts only when the first one finishes.
    ///
    /// Args:
    ///     request: Text and voice parameters to apply.
    ///     cancellationToken: Token that interrupts the current utterance.
    ///
    /// Returns:
    ///     A task completed when the utterance finishes or is canceled.
    /// </summary>
    Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately stops the current utterance and clears the speech queue.
    /// </summary>
    void CancelSpeech();
}

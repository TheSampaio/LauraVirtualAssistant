using Laura.Core.Configuration;
using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>
/// Speech recognition engine (speech-to-text).
///
/// The implementation listens on its own thread and publishes transcriptions through an event,
/// so neither the assistant engine nor the interface blocks while Laura listens.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>
    /// Occurs when the engine produces a transcription.
    ///
    /// The event is raised on a background thread; subscribers that touch the
    /// interface must marshal to the UI thread.
    /// </summary>
    event EventHandler<RecognitionResult>? Recognized;

    /// <summary>
    /// Gets the active listening mode.
    /// </summary>
    RecognitionMode Mode { get; }

    /// <summary>
    /// Gets a value indicating whether the engine could be initialized on this machine.
    ///
    /// It is <see langword="false"/> when there is no microphone or no recognizer
    /// installed; in that case Laura operates only through the interface, without voice.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Initializes the engine and starts listening for wake phrases.
    ///
    /// Args:
    ///     options: Wake phrases, language, and confidence thresholds.
    ///     cancellationToken: Token that aborts startup.
    ///
    /// Returns:
    ///     A task completed when the engine is listening.
    /// </summary>
    Task StartAsync(RecognitionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and releases the audio device.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts shutdown.
    ///
    /// Returns:
    ///     A task completed when the engine has stopped listening.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the listening mode by changing the loaded grammar.
    ///
    /// Args:
    ///     mode: Desired mode.
    ///     cancellationToken: Token that aborts the switch.
    ///
    /// Returns:
    ///     A task completed when the new mode is active.
    /// </summary>
    Task SetModeAsync(RecognitionMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies new options without tearing down the listening session when possible.
    ///
    /// Called when the user changes language or wake phrases in the interface.
    ///
    /// Args:
    ///     options: Updated options.
    ///     cancellationToken: Token that aborts reconfiguration.
    ///
    /// Returns:
    ///     A task completed when the options are in effect.
    /// </summary>
    Task ApplyOptionsAsync(RecognitionOptions options, CancellationToken cancellationToken = default);
}

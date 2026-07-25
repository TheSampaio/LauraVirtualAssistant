using Laura.Core.Text;

namespace Laura.Core.Configuration;

/// <summary>
/// Voice-listening parameters.
/// </summary>
public sealed record RecognitionOptions
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static RecognitionOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether Laura should listen to the microphone.
    ///
    /// Turning it off keeps the assistant usable through the window only.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the language the recognition engine listens in, as a BCP-47 tag.
    ///
    /// Mirrors <see cref="LauraSettings.Culture"/>; it is filled in when settings
    /// are sanitized so the adapter receives everything it needs in one object.
    /// </summary>
    public string Culture { get; init; } = LauraSettings.DefaultCulture;

    /// <summary>
    /// Gets the identifier of the microphone to listen on, or <see langword="null"/>
    /// to use the Windows default capture device.
    ///
    /// The identifier is the device product name reported by the audio stack; it is
    /// matched loosely so the choice survives minor driver-string changes.
    /// </summary>
    public string? MicrophoneDeviceId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the built-in audio pre-processing (noise
    /// suppression and automatic gain) should be requested from the capture device.
    /// </summary>
    public bool NoiseSuppression { get; init; } = true;

    /// <summary>
    /// Gets the phrases that wake the assistant.
    /// </summary>
    public IReadOnlyList<string> WakePhrases { get; init; } =
    [
        "ok laura",
        "okay laura",
        "hey laura",
        "hi laura",
        "hello laura",
        "yo laura",
        "hey there laura",
    ];

    /// <summary>
    /// Gets the minimum confidence, from 0.0 to 1.0, required to accept a transcript.
    ///
    /// Higher values reduce accidental activations at the cost of demanding clearer
    /// diction. The default is deliberately lenient so normal speech is not dropped.
    /// </summary>
    public double MinimumConfidence { get; init; } = 0.35;

    /// <summary>
    /// Gets how long Laura waits for the command after being woken before returning
    /// to listening for the wake word only.
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Gets a value indicating whether the wake word and the command may arrive in
    /// the same phrase, as in "Hey Laura, what time is it".
    /// </summary>
    public bool AllowInlineCommand { get; init; } = true;

    /// <summary>
    /// Gets the known command phrases, used to build a constrained recognition
    /// grammar that is far more accurate than free dictation.
    ///
    /// This is derived from the active skills at runtime and is never persisted.
    /// </summary>
    public IReadOnlyList<string> CommandPhrases { get; init; } = [];

    /// <summary>
    /// Returns a sanitized copy, without empty or duplicate phrases and with the
    /// numeric values clamped to their accepted ranges.
    ///
    /// Returns:
    ///     Options valid to hand to the recognizer. If every wake phrase is invalid,
    ///     the defaults are restored — a Laura with no wake word would be silently deaf.
    /// </summary>
    public RecognitionOptions Sanitized()
    {
        List<string> phrases = [.. WakePhrases
            .Where(static phrase => !string.IsNullOrWhiteSpace(phrase))
            .Select(static phrase => phrase.Trim())
            .Where(static phrase => TextNormalizer.Normalize(phrase).Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        return this with
        {
            WakePhrases = phrases.Count > 0 ? phrases : Default.WakePhrases,
            MicrophoneDeviceId = string.IsNullOrWhiteSpace(MicrophoneDeviceId) ? null : MicrophoneDeviceId.Trim(),
            MinimumConfidence = Math.Clamp(MinimumConfidence, 0.0, 1.0),
            CommandTimeout = TimeSpan.FromSeconds(Math.Clamp(CommandTimeout.TotalSeconds, 3.0, 30.0)),
        };
    }
}

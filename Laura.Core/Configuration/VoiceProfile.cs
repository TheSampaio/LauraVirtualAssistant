namespace Laura.Core.Configuration;

/// <summary>
/// Voice timbre parameters for Laura.
///
/// The ranges follow the SAPI convention for rate and pitch (-10 to 10, with 0 as
/// neutral) because that is the scale the user manipulates directly in the UI;
/// adapters for other engines translate from there.
/// </summary>
public sealed record VoiceProfile
{
    /// <summary>Lowest accepted value for <see cref="Rate"/> and <see cref="Pitch"/>.</summary>
    public const int MinimumProsody = -10;

    /// <summary>Highest accepted value for <see cref="Rate"/> and <see cref="Pitch"/>.</summary>
    public const int MaximumProsody = 10;

    /// <summary>
    /// Gets the default profile, used on first run and when restoring settings.
    /// </summary>
    public static VoiceProfile Default { get; } = new();

    /// <summary>
    /// Gets the name of the voice selected in the synthesis engine.
    ///
    /// When null, the adapter automatically selects the best available female
    /// voice for the active culture.
    /// </summary>
    public string? VoiceName { get; init; }

    /// <summary>Gets the speech rate, from -10 (slow) to 10 (fast).</summary>
    public int Rate { get; init; }

    /// <summary>Gets the voice pitch, from -10 (low) to 10 (high).</summary>
    public int Pitch { get; init; }

    /// <summary>Gets the speech volume, from 0 to 100.</summary>
    public int Volume { get; init; } = 100;

    /// <summary>
    /// Returns a copy with every value clamped to its accepted range.
    ///
    /// Settings read from disk may have been edited by hand; sanitizing on read
    /// keeps an absurd value from crashing the synthesis engine.
    ///
    /// Returns:
    ///     An equivalent profile with <see cref="Rate"/>, <see cref="Pitch"/> and
    ///     <see cref="Volume"/> adjusted to their valid ranges.
    /// </summary>
    public VoiceProfile Sanitized() => this with
    {
        VoiceName = string.IsNullOrWhiteSpace(VoiceName) ? null : VoiceName.Trim(),
        Rate = Math.Clamp(Rate, MinimumProsody, MaximumProsody),
        Pitch = Math.Clamp(Pitch, MinimumProsody, MaximumProsody),
        Volume = Math.Clamp(Volume, 0, 100),
    };
}

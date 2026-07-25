using System.Globalization;

namespace Laura.Core.Configuration;

/// <summary>
/// The complete assistant configuration, persisted to disk and editable from the UI.
/// </summary>
public sealed record LauraSettings
{
    /// <summary>
    /// Culture used when nothing has been configured.
    ///
    /// Laura is English-only, so persisted values from older multilingual builds are
    /// normalized back to this culture.
    /// </summary>
    public const string DefaultCulture = "en-US";

    /// <summary>
    /// Gets the default settings applied on first run.
    /// </summary>
    public static LauraSettings Default { get; } = new();

    /// <summary>
    /// Gets the speech and response culture, as a BCP-47 tag.
    /// </summary>
    public string Culture { get; init; } = DefaultCulture;

    /// <summary>
    /// Gets the name Laura uses to address the user.
    ///
    /// When empty, the assistant falls back to the operating-system account name,
    /// so a fresh install still greets the user by name.
    /// </summary>
    public string UserNickname { get; init; } = string.Empty;

    /// <summary>Gets the voice timbre.</summary>
    public VoiceProfile Voice { get; init; } = VoiceProfile.Default;

    /// <summary>Gets the voice-listening parameters.</summary>
    public RecognitionOptions Recognition { get; init; } = RecognitionOptions.Default;

    /// <summary>Gets the optional generative-mode configuration.</summary>
    public GenerativeAiOptions GenerativeAi { get; init; } = GenerativeAiOptions.Default;

    /// <summary>
    /// Gets a value indicating whether Laura greets the user on startup.
    /// </summary>
    public bool GreetOnStartup { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether Laura announces every full hour.
    /// </summary>
    public bool AnnounceHourly { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the application should start with Windows.
    /// </summary>
    public bool StartWithWindows { get; init; }

    /// <summary>
    /// Resolves the configured culture to a <see cref="CultureInfo"/>.
    ///
    /// Returns:
    ///     The culture matching <see cref="Culture"/>, or the default culture when
    ///     the persisted value does not match any known culture.
    /// </summary>
    public CultureInfo ResolveCulture()
    {
        _ = Culture;
        return CultureInfo.GetCultureInfo(DefaultCulture);
    }

    /// <summary>
    /// Returns a copy with every section sanitized.
    ///
    /// Returns:
    ///     Settings safe to use, with out-of-range values corrected.
    /// </summary>
    public LauraSettings Sanitized()
    {
        return this with
        {
            Culture = DefaultCulture,
            UserNickname = UserNickname.Trim(),
            Voice = Voice.Sanitized(),
            Recognition = Recognition.Sanitized() with { Culture = DefaultCulture },
            GenerativeAi = GenerativeAi.Sanitized(),
        };
    }
}

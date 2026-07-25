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
    /// English is the default because the Windows female English voices sound far
    /// more natural than the Portuguese ones, and because English speech recognition
    /// ships on most machines.
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
        try
        {
            return CultureInfo.GetCultureInfo(Culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(Default.Culture);
        }
    }

    /// <summary>
    /// Returns a copy with every section sanitized.
    ///
    /// Returns:
    ///     Settings safe to use, with out-of-range values corrected.
    /// </summary>
    public LauraSettings Sanitized()
    {
        string culture = ResolveCulture().Name;

        return this with
        {
            Culture = culture,
            UserNickname = UserNickname.Trim(),
            Voice = Voice.Sanitized(),
            Recognition = Recognition.Sanitized() with { Culture = culture },
        };
    }
}

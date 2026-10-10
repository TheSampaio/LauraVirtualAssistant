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
    /// English remains the default; unsupported language tags fall back to it.
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

    /// <summary>Gets the independently selected interface language.</summary>
    public string UiCulture { get; init; } = DefaultCulture;
    /// <summary>Gets the microphone index, or -1 for the system default.</summary>
    public int InputDevice { get; init; } = -1;
    /// <summary>Gets the playback index, or -1 for the system default.</summary>
    public int OutputDevice { get; init; } = -1;
    /// <summary>Gets whether microphone capture is disabled.</summary>
    public bool MicrophoneMuted { get; init; } = true;

    /// <summary>Gets whether local conversation history should persist across sessions.</summary>
    public bool RememberConversations { get; init; } = true;

    /// <summary>
    /// Gets the name Laura uses to address the user.
    ///
    /// When empty, the assistant falls back to the operating-system account name,
    /// so a fresh install still greets the user by name.
    /// </summary>
    public string UserNickname { get; init; } = string.Empty;

    /// <summary>Gets the voice timbre.</summary>
    public VoiceProfile Voice { get; init; } = VoiceProfile.Default;

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
        return CultureInfo.GetCultureInfo(SanitizeCulture(Culture));
    }

    /// <summary>Resolves the independent presentation language with English fallback.</summary>
    public CultureInfo ResolveUiCulture() => CultureInfo.GetCultureInfo(SanitizeCulture(UiCulture));

    /// <summary>Normalizes supported locales without interpreting arbitrary external tags.</summary>
    public static string SanitizeCulture(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "pt-br" => "pt-BR",
        "en-us" => "en-US",
        _ => DefaultCulture,
    };

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
            Culture = SanitizeCulture(Culture),
            UiCulture = SanitizeCulture(UiCulture),
            InputDevice = Math.Max(-1, InputDevice),
            OutputDevice = Math.Max(-1, OutputDevice),
            UserNickname = UserNickname.Trim(),
            Voice = Voice.Sanitized(),
            GenerativeAi = GenerativeAi.Sanitized(),
        };
    }
}

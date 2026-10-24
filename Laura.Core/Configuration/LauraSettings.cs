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
    /// <summary>Gets whether the user explicitly enabled local double-clap microphone detection.</summary>
    public bool ClapWakeEnabled { get; init; }
    /// <summary>Gets local clap sensitivity from zero to one hundred.</summary>
    public int ClapWakeSensitivity { get; init; } = 50;

    /// <summary>Gets whether local conversation history should persist across sessions.</summary>
    public bool RememberConversations { get; init; } = true;
    /// <summary>Gets portable Brain location, learning, retrieval and local embedding options.</summary>
    public Laura.Core.Brain.BrainOptions Brain { get; init; } = new();
    /// <summary>Gets independent local approval and audit settings.</summary>
    public Laura.Core.Permissions.PermissionOptions Permissions { get; init; } = new();
    /// <summary>Gets local screenshot capture and global shortcut preferences.</summary>
    public Laura.Core.Screenshots.ScreenshotOptions Screenshots { get; init; } = new();

    /// <summary>
    /// Gets the name Laura uses to address the user.
    ///
    /// When empty, the assistant falls back to the operating-system account name,
    /// so a fresh install still greets the user by name.
    /// </summary>
    public string UserNickname { get; init; } = string.Empty;
    /// <summary>Gets an optional city for weather queries; empty uses provider IP detection.</summary>
    public string WeatherLocation { get; init; } = string.Empty;

    /// <summary>Gets the voice timbre.</summary>
    public VoiceProfile Voice { get; init; } = VoiceProfile.Default;

    /// <summary>Gets the optional generative-mode configuration.</summary>
    public GenerativeAiOptions GenerativeAi { get; init; } = GenerativeAiOptions.Default;
    /// <summary>Gets optional account-authorized ChatGPT settings, independent of local voice settings.</summary>
    public ChatGptOptions ChatGpt { get; init; } = new();
    /// <summary>Gets explicit local model installation and resource settings.</summary>
    public LocalModelOptions LocalModel { get; init; } = new();
    /// <summary>Gets provider-independent local speech assets.</summary>
    public LocalVoiceOptions LocalVoice { get; init; } = new();
    /// <summary>Gets the native manual or explicitly authorized automatic routing mode.</summary>
    public ConversationProviderSelection ProviderSelection { get; init; } = ConversationProviderSelection.Automatic;
    /// <summary>Gets cumulative autonomous task budgets, separate from individual model and tool deadlines.</summary>
    public CognitiveTaskOptions CognitiveTasks { get; init; } = new();
    /// <summary>Gets the local diagnostic detail threshold; no level includes private payloads.</summary>
    public Microsoft.Extensions.Logging.LogLevel DiagnosticLevel { get; init; } = Microsoft.Extensions.Logging.LogLevel.Information;

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
            ClapWakeSensitivity = Math.Clamp(ClapWakeSensitivity, 0, 100),
            UserNickname = UserNickname.Trim(),
            WeatherLocation = SanitizeWeatherLocation(WeatherLocation),
            Voice = Voice.Sanitized(),
            GenerativeAi = GenerativeAi.Sanitized(),
            ChatGpt = ChatGpt.Sanitized(),
            LocalModel = LocalModel.Sanitized(),
            LocalVoice = LocalVoice.Sanitized(),
            CognitiveTasks = CognitiveTasks.Sanitized(),
            ProviderSelection = Enum.IsDefined(ProviderSelection) ? ProviderSelection : ConversationProviderSelection.Automatic,
            DiagnosticLevel = Enum.IsDefined(DiagnosticLevel) ? DiagnosticLevel : Microsoft.Extensions.Logging.LogLevel.Information,
            Brain = Brain.Sanitized(),
            Permissions = Permissions.Sanitized(),
            Screenshots = Screenshots.Sanitized(),
        };
    }

    /// <summary>Bounds a user-entered weather place to a single short line.</summary>
    private static string SanitizeWeatherLocation(string? value)
    {
        string cleaned = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return cleaned.Length <= 100 ? cleaned : cleaned[..100];
    }
}

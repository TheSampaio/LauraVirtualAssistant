using System.Text;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Configuration;

/// <summary>
/// Persists settings to a <c>settings.ini</c> file under the user's Documents folder.
///
/// Writing goes through a temporary file and only then replaces the real one, so a
/// crash mid-write cannot leave Laura without a configuration.
/// </summary>
public sealed class IniSettingsStore : ISettingsStore
{
    private const string FileHeader =
        "Laura Virtual Assistant settings.\n" +
        "This file can be edited by hand; out-of-range values are corrected on load.";

    /// <summary>
    /// Write encoding, with a byte-order mark.
    ///
    /// The file is meant to be edited by hand; without the mark, editors and
    /// terminals that assume the local code page show accented characters garbled.
    /// </summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: true);

    private const string GeneralSection = "General";
    private const string UserSection = "User";
    private const string VoiceSection = "Voice";
    private const string GenerativeAiSection = "GenerativeAI";

    private readonly string _filePath;
    private readonly ILogger<IniSettingsStore> _logger;

    /// <summary>
    /// Initializes the store pointing at the given file.
    ///
    /// Args:
    ///     filePath: Full path of the configuration file.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public IniSettingsStore(string filePath, ILogger<IniSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// Returns the default configuration file path under the user's Documents folder.
    ///
    /// Returns:
    ///     The full path of <c>Documents/Laura Virtual Assistant/settings.ini</c>.
    /// </summary>
    public static string GetDefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Laura Virtual Assistant",
        "settings.ini");

    /// <inheritdoc />
    public async Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No settings saved at {Path}; using the defaults.", _filePath);
            return LauraSettings.Default;
        }

        try
        {
            string content = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return ReadSettings(IniDocument.Parse(content)).Sanitized();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable configuration must not stop Laura from starting.
            _logger.LogWarning(exception, "Failed to read {Path}; using the defaults.", _filePath);
            return LauraSettings.Default;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(LauraSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"Invalid configuration path: {_filePath}");

        Directory.CreateDirectory(directory);

        string temporaryPath = _filePath + ".tmp";
        string content = WriteSettings(settings).ToIniString(FileHeader);

        await File.WriteAllTextAsync(temporaryPath, content, FileEncoding, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, _filePath, overwrite: true);

        _logger.LogDebug("Settings written to {Path}.", _filePath);
    }

    /// <summary>
    /// Converts an INI document into settings.
    ///
    /// Args:
    ///     document: Document read from disk.
    ///
    /// Returns:
    ///     The settings, with defaults filling in whatever is missing.
    /// </summary>
    private static LauraSettings ReadSettings(IniDocument document)
    {
        LauraSettings defaults = LauraSettings.Default;

        return new LauraSettings
        {
            DiagnosticLevel = (Microsoft.Extensions.Logging.LogLevel)document.GetInt32("Diagnostics", "MinimumLevel", 2),
            Culture = document.GetString(GeneralSection, "Language", defaults.Culture),
            UiCulture = document.GetString(GeneralSection, "UiLanguage", defaults.UiCulture),
            InputDevice = document.GetInt32(VoiceSection, "InputDevice", defaults.InputDevice),
            OutputDevice = document.GetInt32(VoiceSection, "OutputDevice", defaults.OutputDevice),
            MicrophoneMuted = document.GetBoolean(VoiceSection, "MicrophoneMuted", defaults.MicrophoneMuted),
            ClapWakeEnabled = document.GetBoolean(VoiceSection, "ClapWakeEnabled", defaults.ClapWakeEnabled),
            ClapWakeSensitivity = document.GetInt32(VoiceSection, "ClapWakeSensitivity", defaults.ClapWakeSensitivity),
            RememberConversations = document.GetBoolean(GeneralSection, "RememberConversations", defaults.RememberConversations),
            Permissions = new Laura.Core.Permissions.PermissionOptions
            {
                Mode = (Laura.Core.Permissions.ApprovalMode)document.GetInt32("Permissions", "Mode", 0),
                OpenInstalledApplications = document.GetBoolean("Permissions", "OpenInstalledApplications", true),
                Voice = (Laura.Core.Permissions.ApprovalVoiceMode)document.GetInt32("Permissions", "Voice", 1),
                Notifications = document.GetBoolean("Permissions", "Notifications", true),
                TimeoutSeconds = document.GetInt32("Permissions", "TimeoutSeconds", 120),
                HistoryDays = document.GetInt32("Permissions", "HistoryDays", 30)
            },
            Screenshots = new Laura.Core.Screenshots.ScreenshotOptions
            {
                ShortcutsEnabled = document.GetBoolean("Screenshot", "ShortcutsEnabled", true), AllDisplays = document.GetBoolean("Screenshot", "AllDisplays", true),
                RegionShortcut = document.GetString("Screenshot", "RegionShortcut", new Laura.Core.Screenshots.ScreenshotOptions().RegionShortcut), WindowShortcut = document.GetString("Screenshot", "WindowShortcut", new Laura.Core.Screenshots.ScreenshotOptions().WindowShortcut),
                FullScreenShortcut = document.GetString("Screenshot", "FullScreenShortcut", new Laura.Core.Screenshots.ScreenshotOptions().FullScreenShortcut)
            },
            Brain = new Laura.Core.Brain.BrainOptions
            {
                Directory = document.GetString("Brain", "Directory", ""),
                AutoLearn = document.GetBoolean("Brain", "AutoLearn", true),
                MaintenanceEnabled = document.GetBoolean("Brain", "MaintenanceEnabled", false),
                ContextCharacters = document.GetInt32("Brain", "ContextCharacters", 5000),
                ReconciliationSeconds = document.GetInt32("Brain", "ReconciliationSeconds", 300),
                EmbeddingModel = document.GetString("Brain", "EmbeddingModel", ""),
                EmbeddingVocabulary = document.GetString("Brain", "EmbeddingVocabulary", ""),
                LexicalWeight = document.GetDouble("Brain", "LexicalWeight", .45), SemanticWeight = document.GetDouble("Brain", "SemanticWeight", .3),
                GraphWeight = document.GetDouble("Brain", "GraphWeight", .15), TemporalWeight = document.GetDouble("Brain", "TemporalWeight", .1),
                GraphLimit = document.GetInt32("Brain", "GraphLimit", 400),
                RetrievalGraphDepth = document.GetInt32("Brain", "RetrievalGraphDepth", 2), RetrievalGraphCandidates = document.GetInt32("Brain", "RetrievalGraphCandidates", 160),
                RetrievalGraphMilliseconds = document.GetInt32("Brain", "RetrievalGraphMilliseconds", 30)
                , TrustedSkills = document.GetString("Brain", "TrustedSkills", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            },
            GreetOnStartup = document.GetBoolean(GeneralSection, "GreetOnStartup", defaults.GreetOnStartup),
            AnnounceHourly = document.GetBoolean(GeneralSection, "AnnounceHourly", defaults.AnnounceHourly),
            StartWithWindows = document.GetBoolean(GeneralSection, "StartWithWindows", defaults.StartWithWindows),

            UserNickname = document.GetString(UserSection, "Nickname", defaults.UserNickname),

            Voice = new VoiceProfile
            {
                VoiceName = document.GetString(VoiceSection, "Voice", string.Empty) is { Length: > 0 } voice
                    ? voice
                    : null,
                Rate = document.GetInt32(VoiceSection, "Rate", defaults.Voice.Rate),
                Pitch = document.GetInt32(VoiceSection, "Pitch", defaults.Voice.Pitch),
                Volume = document.GetInt32(VoiceSection, "Volume", defaults.Voice.Volume),
            },

            GenerativeAi = new GenerativeAiOptions
            {
                Enabled = document.GetBoolean(GenerativeAiSection, "Enabled", defaults.GenerativeAi.Enabled),
                Provider = document.GetString(GenerativeAiSection, "Provider", defaults.GenerativeAi.Provider),
                Model = document.GetString(GenerativeAiSection, "Model", defaults.GenerativeAi.Model),
                Persona = document.GetString(GenerativeAiSection, "Persona", defaults.GenerativeAi.Persona),
                LiveVoice = document.GetString(GenerativeAiSection, "LiveVoice", defaults.GenerativeAi.LiveVoice),
                ContinuousListening = document.GetBoolean(GenerativeAiSection, "ContinuousListening", false),
                SilenceDurationMs = document.GetInt32(GenerativeAiSection, "SilenceDurationMs", defaults.GenerativeAi.SilenceDurationMs),
            },
            ChatGpt = new ChatGptOptions
            {
                Enabled = document.GetBoolean("ChatGPT", "Enabled", false), Model = document.GetString("ChatGPT", "Model", ""),
                ShareContext = document.GetBoolean("ChatGPT", "ShareContext", false), AllowAutomatic = document.GetBoolean("ChatGPT", "AllowAutomatic", false),
                IncludedPlanOnly = document.GetBoolean("ChatGPT", "IncludedPlanOnly", false),
                Registration = Guid.TryParse(document.GetString("ChatGPT", "Registration", ""), out var registration) && registration != Guid.Empty ? registration : null
            },
            ProviderSelection = Enum.TryParse<ConversationProviderSelection>(document.GetString("ChatGPT", "Selection", "Gemini"), out var selection) ? selection : ConversationProviderSelection.Gemini,
            CognitiveTasks = new CognitiveTaskOptions
            {
                MaximumCycles = document.GetInt32("CognitiveTasks", "MaximumCycles", 32), ActiveMinutes = document.GetInt32("CognitiveTasks", "ActiveMinutes", 10),
                MaximumToolCalls = document.GetInt32("CognitiveTasks", "MaximumToolCalls", 128), MaximumRecoveries = document.GetInt32("CognitiveTasks", "MaximumRecoveries", 2),
                MaximumResultMegabytes = document.GetInt32("CognitiveTasks", "MaximumResultMegabytes", 16),
                MaximumProviderCalls = document.GetInt32("CognitiveTasks", "MaximumProviderCalls", 64), IdleMinutes = document.GetInt32("CognitiveTasks", "IdleMinutes", 1440),
                ToolTimeoutSeconds = document.GetInt32("CognitiveTasks", "ToolTimeoutSeconds", 120)
            },
        };
    }

    /// <summary>
    /// Converts settings into an INI document.
    ///
    /// Args:
    ///     settings: Settings to serialize.
    ///
    /// Returns:
    ///     The document ready to write.
    /// </summary>
    private static IniDocument WriteSettings(LauraSettings settings)
    {
        var document = new IniDocument();

        document.Set(GeneralSection, "Language", settings.Culture);
        document.Set(GeneralSection, "UiLanguage", settings.UiCulture);
        document.SetNumber(VoiceSection, "InputDevice", settings.InputDevice);
        document.SetNumber(VoiceSection, "OutputDevice", settings.OutputDevice);
        document.SetBoolean(VoiceSection, "MicrophoneMuted", settings.MicrophoneMuted);
        document.SetBoolean(VoiceSection, "ClapWakeEnabled", settings.ClapWakeEnabled);
        document.SetNumber(VoiceSection, "ClapWakeSensitivity", settings.ClapWakeSensitivity);
        document.SetBoolean(GeneralSection, "RememberConversations", settings.RememberConversations);
        document.SetNumber("Permissions", "Mode", (int)settings.Permissions.Mode);
        document.SetBoolean("Permissions", "OpenInstalledApplications", settings.Permissions.OpenInstalledApplications);
        document.SetNumber("Permissions", "Voice", (int)settings.Permissions.Voice);
        document.SetBoolean("Permissions", "Notifications", settings.Permissions.Notifications);
        document.SetNumber("Permissions", "TimeoutSeconds", settings.Permissions.TimeoutSeconds);
        document.SetNumber("Permissions", "HistoryDays", settings.Permissions.HistoryDays);
        document.SetBoolean("Screenshot", "ShortcutsEnabled", settings.Screenshots.ShortcutsEnabled); document.SetBoolean("Screenshot", "AllDisplays", settings.Screenshots.AllDisplays);
        document.Set("Screenshot", "RegionShortcut", settings.Screenshots.RegionShortcut); document.Set("Screenshot", "WindowShortcut", settings.Screenshots.WindowShortcut);
        document.Set("Screenshot", "FullScreenShortcut", settings.Screenshots.FullScreenShortcut);
        document.Set("Brain", "Directory", settings.Brain.Directory);
        document.SetBoolean("Brain", "AutoLearn", settings.Brain.AutoLearn);
        document.SetBoolean("Brain", "MaintenanceEnabled", settings.Brain.MaintenanceEnabled);
        document.SetNumber("Brain", "ContextCharacters", settings.Brain.ContextCharacters);
        document.SetNumber("Brain", "ReconciliationSeconds", settings.Brain.ReconciliationSeconds);
        document.Set("Brain", "EmbeddingModel", settings.Brain.EmbeddingModel);
        document.Set("Brain", "EmbeddingVocabulary", settings.Brain.EmbeddingVocabulary);
        document.SetNumber("Brain", "LexicalWeight", settings.Brain.LexicalWeight);
        document.SetNumber("Brain", "SemanticWeight", settings.Brain.SemanticWeight);
        document.SetNumber("Brain", "GraphWeight", settings.Brain.GraphWeight);
        document.SetNumber("Brain", "TemporalWeight", settings.Brain.TemporalWeight);
        document.SetNumber("Brain", "GraphLimit", settings.Brain.GraphLimit);
        document.SetNumber("Brain", "RetrievalGraphDepth", settings.Brain.RetrievalGraphDepth);
        document.SetNumber("Brain", "RetrievalGraphCandidates", settings.Brain.RetrievalGraphCandidates);
        document.SetNumber("Brain", "RetrievalGraphMilliseconds", settings.Brain.RetrievalGraphMilliseconds);
        document.Set("Brain", "TrustedSkills", string.Join(';', settings.Brain.TrustedSkills));
        document.SetBoolean(GeneralSection, "GreetOnStartup", settings.GreetOnStartup);
        document.SetBoolean(GeneralSection, "AnnounceHourly", settings.AnnounceHourly);
        document.SetBoolean(GeneralSection, "StartWithWindows", settings.StartWithWindows);

        document.Set(UserSection, "Nickname", settings.UserNickname);

        document.Set(VoiceSection, "Voice", settings.Voice.VoiceName ?? string.Empty);
        document.SetNumber(VoiceSection, "Rate", settings.Voice.Rate);
        document.SetNumber(VoiceSection, "Pitch", settings.Voice.Pitch);
        document.SetNumber(VoiceSection, "Volume", settings.Voice.Volume);

        document.SetBoolean(GenerativeAiSection, "Enabled", settings.GenerativeAi.Enabled);
        document.SetBoolean("ChatGPT", "Enabled", settings.ChatGpt.Enabled); document.Set("ChatGPT", "Model", settings.ChatGpt.Model);
        document.SetBoolean("ChatGPT", "ShareContext", settings.ChatGpt.ShareContext); document.SetBoolean("ChatGPT", "AllowAutomatic", settings.ChatGpt.AllowAutomatic);
        document.SetBoolean("ChatGPT", "IncludedPlanOnly", settings.ChatGpt.IncludedPlanOnly); document.Set("ChatGPT", "Selection", settings.ProviderSelection.ToString());
        document.Set("ChatGPT", "Registration", settings.ChatGpt.Registration?.ToString("N") ?? "");
        document.SetNumber("CognitiveTasks", "MaximumCycles", settings.CognitiveTasks.MaximumCycles); document.SetNumber("CognitiveTasks", "ActiveMinutes", settings.CognitiveTasks.ActiveMinutes);
        document.SetNumber("CognitiveTasks", "MaximumToolCalls", settings.CognitiveTasks.MaximumToolCalls); document.SetNumber("CognitiveTasks", "MaximumRecoveries", settings.CognitiveTasks.MaximumRecoveries);
        document.SetNumber("CognitiveTasks", "MaximumResultMegabytes", settings.CognitiveTasks.MaximumResultMegabytes);
        document.SetNumber("CognitiveTasks", "MaximumProviderCalls", settings.CognitiveTasks.MaximumProviderCalls); document.SetNumber("CognitiveTasks", "IdleMinutes", settings.CognitiveTasks.IdleMinutes);
        document.SetNumber("CognitiveTasks", "ToolTimeoutSeconds", settings.CognitiveTasks.ToolTimeoutSeconds);
        document.SetNumber("Diagnostics", "MinimumLevel", (int)settings.DiagnosticLevel);
        document.Set(GenerativeAiSection, "Provider", settings.GenerativeAi.Provider);
        document.Set(GenerativeAiSection, "Model", settings.GenerativeAi.Model);
        document.Set(GenerativeAiSection, "Persona", settings.GenerativeAi.Persona);
        document.Set(GenerativeAiSection, "LiveVoice", settings.GenerativeAi.LiveVoice);
        document.SetBoolean(GenerativeAiSection, "ContinuousListening", settings.GenerativeAi.ContinuousListening);
        document.SetNumber(GenerativeAiSection, "SilenceDurationMs", settings.GenerativeAi.SilenceDurationMs);

        return document;
    }
}

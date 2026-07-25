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
    private const string RecognitionSection = "Recognition";
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
            return ReadSettings(IniDocument.Parse(content));
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
            Culture = document.GetString(GeneralSection, "Language", defaults.Culture),
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

            Recognition = new RecognitionOptions
            {
                Enabled = document.GetBoolean(RecognitionSection, "Enabled", defaults.Recognition.Enabled),
                MicrophoneDeviceId = document.GetString(RecognitionSection, "Microphone", string.Empty) is { Length: > 0 } mic
                    ? mic
                    : null,
                NoiseSuppression = document.GetBoolean(
                    RecognitionSection,
                    "NoiseSuppression",
                    defaults.Recognition.NoiseSuppression),
                WakePhrases = document.GetList(
                    RecognitionSection,
                    "WakePhrases",
                    defaults.Recognition.WakePhrases),
                MinimumConfidence = document.GetDouble(
                    RecognitionSection,
                    "MinimumConfidence",
                    defaults.Recognition.MinimumConfidence),
                CommandTimeout = TimeSpan.FromSeconds(document.GetDouble(
                    RecognitionSection,
                    "CommandTimeoutSeconds",
                    defaults.Recognition.CommandTimeout.TotalSeconds)),
                AllowInlineCommand = document.GetBoolean(
                    RecognitionSection,
                    "AcceptCommandWithWakeWord",
                    defaults.Recognition.AllowInlineCommand),
            },

            GenerativeAi = new GenerativeAiOptions
            {
                Enabled = document.GetBoolean(GenerativeAiSection, "Enabled", defaults.GenerativeAi.Enabled),
                Provider = document.GetString(GenerativeAiSection, "Provider", defaults.GenerativeAi.Provider),
                Endpoint = document.GetString(GenerativeAiSection, "Endpoint", defaults.GenerativeAi.Endpoint),
                Model = document.GetString(GenerativeAiSection, "Model", defaults.GenerativeAi.Model),
                Persona = document.GetString(GenerativeAiSection, "Persona", defaults.GenerativeAi.Persona),
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
        document.SetBoolean(GeneralSection, "GreetOnStartup", settings.GreetOnStartup);
        document.SetBoolean(GeneralSection, "AnnounceHourly", settings.AnnounceHourly);
        document.SetBoolean(GeneralSection, "StartWithWindows", settings.StartWithWindows);

        document.Set(UserSection, "Nickname", settings.UserNickname);

        document.Set(VoiceSection, "Voice", settings.Voice.VoiceName ?? string.Empty);
        document.SetNumber(VoiceSection, "Rate", settings.Voice.Rate);
        document.SetNumber(VoiceSection, "Pitch", settings.Voice.Pitch);
        document.SetNumber(VoiceSection, "Volume", settings.Voice.Volume);

        document.SetBoolean(RecognitionSection, "Enabled", settings.Recognition.Enabled);
        document.Set(RecognitionSection, "Microphone", settings.Recognition.MicrophoneDeviceId ?? string.Empty);
        document.SetBoolean(RecognitionSection, "NoiseSuppression", settings.Recognition.NoiseSuppression);
        document.SetList(RecognitionSection, "WakePhrases", settings.Recognition.WakePhrases);
        document.SetNumber(RecognitionSection, "MinimumConfidence", settings.Recognition.MinimumConfidence);
        document.SetNumber(RecognitionSection, "CommandTimeoutSeconds", settings.Recognition.CommandTimeout.TotalSeconds);
        document.SetBoolean(RecognitionSection, "AcceptCommandWithWakeWord", settings.Recognition.AllowInlineCommand);

        document.SetBoolean(GenerativeAiSection, "Enabled", settings.GenerativeAi.Enabled);
        document.Set(GenerativeAiSection, "Provider", settings.GenerativeAi.Provider);
        document.Set(GenerativeAiSection, "Endpoint", settings.GenerativeAi.Endpoint);
        document.Set(GenerativeAiSection, "Model", settings.GenerativeAi.Model);
        document.Set(GenerativeAiSection, "Persona", settings.GenerativeAi.Persona);

        return document;
    }
}

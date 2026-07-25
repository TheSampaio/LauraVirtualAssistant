using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;

namespace Laura.App.Views;

/// <summary>
/// An editable copy of the settings while the window is open.
///
/// The form works on this copy and only turns it back into a
/// <see cref="LauraSettings"/> when saving; that way "Cancel" is simply discarding
/// the model, with nothing to undo.
/// </summary>
public sealed class SettingsEditModel
{
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ILocalizer _localizer;
    private readonly IModelCatalog _modelCatalog;
    private readonly IAudioDeviceCatalog _audioDevices;

    /// <summary>
    /// Creates the model from the current settings.
    ///
    /// Args:
    ///     settings: Current settings, copied into the editable fields.
    ///     synthesizer: Synthesis engine, queried to list the voices.
    ///     localizer: Localizer, queried to list the languages.
    ///     modelCatalog: Catalog of installed generative models.
    ///     audioDevices: Catalog of capture devices.
    /// </summary>
    public SettingsEditModel(
        LauraSettings settings,
        ISpeechSynthesizer synthesizer,
        ILocalizer localizer,
        IModelCatalog modelCatalog,
        IAudioDeviceCatalog audioDevices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(modelCatalog);
        ArgumentNullException.ThrowIfNull(audioDevices);

        _synthesizer = synthesizer;
        _localizer = localizer;
        _modelCatalog = modelCatalog;
        _audioDevices = audioDevices;

        Culture = settings.Culture;
        UserNickname = settings.UserNickname;
        VoiceName = settings.Voice.VoiceName;
        Rate = settings.Voice.Rate;
        Pitch = settings.Voice.Pitch;
        Volume = settings.Voice.Volume;

        RecognitionEnabled = settings.Recognition.Enabled;
        MicrophoneDeviceId = settings.Recognition.MicrophoneDeviceId;
        NoiseSuppression = settings.Recognition.NoiseSuppression;
        WakePhrases = [.. settings.Recognition.WakePhrases];
        MinimumConfidence = settings.Recognition.MinimumConfidence;
        CommandTimeoutSeconds = (int)settings.Recognition.CommandTimeout.TotalSeconds;
        AllowInlineCommand = settings.Recognition.AllowInlineCommand;

        GreetOnStartup = settings.GreetOnStartup;
        AnnounceHourly = settings.AnnounceHourly;
        StartWithWindows = settings.StartWithWindows;

        GenerativeAiEnabled = settings.GenerativeAi.Enabled;
        GenerativeAiEndpoint = settings.GenerativeAi.Endpoint;
        GenerativeAiModel = settings.GenerativeAi.Model;
        GenerativeAiPersona = settings.GenerativeAi.Persona;
    }

    /// <summary>Gets or sets the speech and response culture, as a BCP-47 tag.</summary>
    public string Culture { get; set; }

    /// <summary>Gets or sets the nickname Laura addresses the user by, empty for the account name.</summary>
    public string UserNickname { get; set; }

    /// <summary>Gets or sets the chosen voice, or <see langword="null"/> for automatic.</summary>
    public string? VoiceName { get; set; }

    /// <summary>Gets or sets the speech rate.</summary>
    public int Rate { get; set; }

    /// <summary>Gets or sets the voice pitch.</summary>
    public int Pitch { get; set; }

    /// <summary>Gets or sets the speech volume.</summary>
    public int Volume { get; set; }

    /// <summary>Gets or sets whether Laura listens to the microphone.</summary>
    public bool RecognitionEnabled { get; set; }

    /// <summary>Gets or sets the chosen microphone id, or <see langword="null"/> for the default.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Gets or sets whether the device's built-in noise reduction is requested.</summary>
    public bool NoiseSuppression { get; set; }

    /// <summary>Gets or sets the wake phrases.</summary>
    public IReadOnlyList<string> WakePhrases { get; set; }

    /// <summary>Gets or sets the minimum accepted confidence, from 0.0 to 1.0.</summary>
    public double MinimumConfidence { get; set; }

    /// <summary>Gets or sets the command listening window, in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; }

    /// <summary>Gets or sets whether the command may come with the wake word.</summary>
    public bool AllowInlineCommand { get; set; }

    /// <summary>Gets or sets whether Laura greets the user on startup.</summary>
    public bool GreetOnStartup { get; set; }

    /// <summary>Gets or sets whether Laura announces full hours.</summary>
    public bool AnnounceHourly { get; set; }

    /// <summary>Gets or sets whether the application starts with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Gets or sets whether the generative mode is on.</summary>
    public bool GenerativeAiEnabled { get; set; }

    /// <summary>Gets or sets the generative service endpoint.</summary>
    public string GenerativeAiEndpoint { get; set; }

    /// <summary>Gets or sets the generative model.</summary>
    public string GenerativeAiModel { get; set; }

    /// <summary>Gets or sets the persona passed to the generative model.</summary>
    public string GenerativeAiPersona { get; set; }

    /// <summary>
    /// Lists the natural voices available to the application.
    ///
    /// Voices matching the selected language appear first, but the remaining natural
    /// voices stay selectable so installed defaults like Aria and Francisca are not
    /// hidden while the user is changing languages.
    ///
    /// Returns:
    ///     The voices matching the current culture, or all of them when none matches.
    /// </summary>
    public IReadOnlyList<VoiceDescriptor> GetSelectableVoices()
    {
        IReadOnlyList<VoiceDescriptor> voices = _synthesizer.GetAvailableVoices();
        string language = new CultureInfo(Culture).TwoLetterISOLanguageName;

        List<VoiceDescriptor> matching =
        [
            .. voices.Where(voice => voice.Culture.StartsWith(language, StringComparison.OrdinalIgnoreCase)),
        ];

        return
        [
            .. matching.Concat(voices.Where(voice => !matching.Any(match =>
                string.Equals(match.Name, voice.Name, StringComparison.Ordinal)))),
        ];
    }

    /// <summary>
    /// Lists the languages a translation exists for.
    ///
    /// Returns:
    ///     The available cultures.
    /// </summary>
    public IReadOnlyList<CultureInfo> GetSelectableCultures() => _localizer.AvailableCultures;

    /// <summary>
    /// Lists the installed generative models.
    ///
    /// Returns:
    ///     The models pulled locally by the provider.
    /// </summary>
    public IReadOnlyList<string> GetInstalledModels() => _modelCatalog.GetInstalledModels();

    /// <summary>
    /// Lists the capture devices.
    ///
    /// Returns:
    ///     The available microphones, the first being the system default.
    /// </summary>
    public IReadOnlyList<AudioDevice> GetInputDevices() => _audioDevices.GetInputDevices();

    /// <summary>
    /// Builds the current voice profile, for an immediate preview.
    ///
    /// Returns:
    ///     A profile with the values being edited.
    /// </summary>
    public VoiceProfile BuildVoiceProfile() => new()
    {
        VoiceName = VoiceName,
        Rate = Rate,
        Pitch = Pitch,
        Volume = Volume,
    };

    /// <summary>
    /// Turns the editable model into persistable settings.
    ///
    /// Returns:
    ///     Settings ready for <see cref="ISettingsService.UpdateAsync"/>, which
    ///     still sanitizes them.
    /// </summary>
    public LauraSettings ToSettings() => new()
    {
        Culture = Culture,
        UserNickname = UserNickname,
        Voice = BuildVoiceProfile(),
        Recognition = new RecognitionOptions
        {
            Enabled = RecognitionEnabled,
            Culture = Culture,
            MicrophoneDeviceId = MicrophoneDeviceId,
            NoiseSuppression = NoiseSuppression,
            WakePhrases = WakePhrases,
            MinimumConfidence = MinimumConfidence,
            CommandTimeout = TimeSpan.FromSeconds(CommandTimeoutSeconds),
            AllowInlineCommand = AllowInlineCommand,
        },
        GenerativeAi = new GenerativeAiOptions
        {
            Enabled = GenerativeAiEnabled,
            Endpoint = GenerativeAiEndpoint,
            Model = GenerativeAiModel,
            Persona = GenerativeAiPersona,
        },
        GreetOnStartup = GreetOnStartup,
        AnnounceHourly = AnnounceHourly,
        StartWithWindows = StartWithWindows,
    };
}

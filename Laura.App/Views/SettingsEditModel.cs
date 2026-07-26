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
    private readonly IModelCatalog _modelCatalog;

    /// <summary>
    /// Creates the model from the current settings.
    ///
    /// Args:
    ///     settings: Current settings, copied into the editable fields.
    ///     synthesizer: Synthesis engine, queried to list the voices.
    ///     modelCatalog: Catalog of installed generative models.
    /// </summary>
    public SettingsEditModel(
        LauraSettings settings,
        ISpeechSynthesizer synthesizer,
        IModelCatalog modelCatalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(modelCatalog);

        _synthesizer = synthesizer;
        _modelCatalog = modelCatalog;

        Culture = settings.Culture;
        UserNickname = settings.UserNickname;
        VoiceName = settings.Voice.VoiceName;
        Rate = settings.Voice.Rate;
        Pitch = settings.Voice.Pitch;
        Volume = settings.Voice.Volume;

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
    /// Lists the installed generative models.
    ///
    /// Returns:
    ///     The models pulled locally by the provider.
    /// </summary>
    public IReadOnlyList<string> GetInstalledModels() => _modelCatalog.GetInstalledModels();

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

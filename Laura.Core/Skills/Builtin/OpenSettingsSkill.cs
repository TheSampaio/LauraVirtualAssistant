using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Opens Laura's own settings window.
///
/// It precedes <see cref="LaunchApplicationSkill"/> in the evaluation order because
/// "open settings" would also match the generic open prefix.
/// </summary>
public sealed class OpenSettingsSkill : PhraseSkillBase
{
    private readonly IShellController _shellController;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     shellController: Service that shows the settings window.
    /// </summary>
    public OpenSettingsSkill(ILocalizer localizer, IShellController shellController)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(shellController);
        _shellController = shellController;
    }

    /// <inheritdoc />
    public override string Id => "builtin.openSettings";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.SettingsPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        _shellController.ShowSettings();
        return Task.FromResult(SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.SettingsResponse)));
    }
}

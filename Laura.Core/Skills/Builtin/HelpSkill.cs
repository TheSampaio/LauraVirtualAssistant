using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Says out loud what Laura can do.
/// </summary>
public sealed class HelpSkill : PhraseSkillBase
{
    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    /// </summary>
    public HelpSkill(ILocalizer localizer)
        : base(localizer)
    {
    }

    /// <inheritdoc />
    public override string Id => "builtin.help";

    /// <inheritdoc />
    public override int Priority => 35;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.HelpPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.HelpResponse)));
}

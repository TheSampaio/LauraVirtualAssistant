using Laura.Core.Abstractions;
using Laura.Core.Engine;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Returns the user's greeting.
///
/// It has low priority because its triggers are short and generic ("hi", "good
/// morning"): any more specific skill should be consulted first.
/// </summary>
public sealed class GreetingSkill : PhraseSkillBase
{
    private readonly GreetingComposer _composer;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the trigger phrases.
    ///     composer: Composer of the time-of-day greeting.
    /// </summary>
    public GreetingSkill(ILocalizer localizer, GreetingComposer composer)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        _composer = composer;
    }

    /// <inheritdoc />
    public override string Id => "builtin.greeting";

    /// <inheritdoc />
    public override int Priority => 70;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.GreetingPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SkillResponse.Speak(_composer.ComposeGreeting()));
}

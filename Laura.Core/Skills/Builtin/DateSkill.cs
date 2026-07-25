using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Answers questions about today's date.
/// </summary>
public sealed class DateSkill : PhraseSkillBase
{
    private readonly IClock _clock;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     clock: Source of the current date.
    /// </summary>
    public DateSkill(ILocalizer localizer, IClock clock)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <inheritdoc />
    public override string Id => "builtin.date";

    /// <inheritdoc />
    public override int Priority => 20;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.DatePhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SkillResponse.Speak(
            Localizer.Get(LocalizationKeys.Skills.DateResponse, _clock.Now)));
}

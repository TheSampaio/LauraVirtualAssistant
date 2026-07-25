using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Answers questions about the current time.
/// </summary>
public sealed class TimeSkill : PhraseSkillBase
{
    private readonly IClock _clock;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     clock: Source of the current time.
    /// </summary>
    public TimeSkill(ILocalizer localizer, IClock clock)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <inheritdoc />
    public override string Id => "builtin.time";

    /// <inheritdoc />
    public override int Priority => 20;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.TimePhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SkillResponse.Speak(
            Localizer.Get(LocalizationKeys.Skills.TimeResponse, _clock.Now)));
}

using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Text;

namespace Laura.Core.Skills.Builtin;

/// <summary>Handles an explicit interruption after the engine has canceled the active request and fenced queued turns.</summary>
public sealed class CancelTaskSkill(ILocalizer localizer) : PhraseSkillBase(localizer)
{
    /// <inheritdoc />
    public override string Id => "builtin.cancelTask";
    /// <inheritdoc />
    public override int Priority => 4;
    /// <inheritdoc />
    public override bool ModelCallable => false;
    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.CancelTaskPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.CancelTaskResponse)));

    /// <summary>Matches only a complete owner command before queue admission, so cancellation can preempt inference.</summary>
    public static bool Matches(string text, ILocalizer localizer)
        => PhraseMatcher.MatchesWhole(TextNormalizer.Normalize(text), localizer.GetPhrases(LocalizationKeys.Skills.CancelTaskPhrases));
}

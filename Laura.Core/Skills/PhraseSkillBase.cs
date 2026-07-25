using Laura.Core.Abstractions;

namespace Laura.Core.Skills;

/// <summary>
/// Base for skills activated by a phrase list from the language file.
///
/// It concentrates recognition so each concrete skill contains only what it actually does.
/// </summary>
public abstract class PhraseSkillBase : ISkill
{
    /// <summary>
    /// Initializes the skill with the given localizer.
    ///
    /// Args:
    ///     localizer: Source of the trigger phrases and the responses.
    /// </summary>
    protected PhraseSkillBase(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        Localizer = localizer;
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract int Priority { get; }

    /// <summary>
    /// Gets the localizer used to read phrases and compose responses.
    /// </summary>
    protected ILocalizer Localizer { get; }

    /// <summary>
    /// Gets the key of the phrase list that activates this skill.
    /// </summary>
    protected abstract string PhrasesKey { get; }

    /// <inheritdoc />
    public virtual bool CanHandle(SkillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PhraseMatcher.MatchesAny(request.NormalizedText, Localizer.GetPhrases(PhrasesKey));
    }

    /// <inheritdoc />
    public abstract Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default);
}

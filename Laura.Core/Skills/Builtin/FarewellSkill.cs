using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Shuts the assistant down at the user's request.
/// </summary>
public sealed class FarewellSkill : PhraseSkillBase
{
    private readonly IUserContext _userContext;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     userContext: The name the user is addressed by in the farewell.
    /// </summary>
    public FarewellSkill(ILocalizer localizer, IUserContext userContext)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(userContext);
        _userContext = userContext;
    }

    /// <inheritdoc />
    public override string Id => "builtin.farewell";

    /// <inheritdoc />
    public override int Priority => 40;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.FarewellPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SkillResponse.Farewell(
            Localizer.Get(LocalizationKeys.Skills.FarewellResponse, _userContext.DisplayName)));
}

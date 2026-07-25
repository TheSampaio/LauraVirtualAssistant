using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Locks the workstation.
/// </summary>
public sealed class LockWorkstationSkill : PhraseSkillBase
{
    private readonly ISystemController _systemController;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     systemController: Service that locks the session.
    /// </summary>
    public LockWorkstationSkill(ILocalizer localizer, ISystemController systemController)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(systemController);
        _systemController = systemController;
    }

    /// <inheritdoc />
    public override string Id => "builtin.lockWorkstation";

    /// <inheritdoc />
    public override int Priority => 30;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.LockPhrases;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        // The confirmation is spoken before locking; after it the session audio stops.
        _systemController.LockWorkstation();
        return Task.FromResult(SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.LockResponse)));
    }
}

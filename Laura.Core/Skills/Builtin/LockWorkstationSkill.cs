using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Permissions;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Locks the workstation.
/// </summary>
public sealed class LockWorkstationSkill : PhraseSkillBase
{
    private readonly ISystemController _systemController;
    private readonly PermissionManager _permissions;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     systemController: Service that locks the session.
    ///     permissions: Trusted native owner authorization boundary.
    /// </summary>
    public LockWorkstationSkill(ILocalizer localizer, ISystemController systemController, PermissionManager permissions)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(systemController);
        _systemController = systemController;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public override string Id => "builtin.lockWorkstation";

    /// <inheritdoc />
    public override int Priority => 30;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.LockPhrases;

    /// <inheritdoc />
    public override async Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        var action = new PermissionAction(PermissionOperation.SystemSettings, "Native lock skill", request.RawText,
            parameters: "lock", taskId: request.TurnId);
        bool accepted = await _permissions.ExecuteAsync(action, _ => Task.FromResult(_systemController.LockWorkstation()),
            static result => result, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(Localizer.Get(accepted ? LocalizationKeys.Skills.LockResponse : LocalizationKeys.Assistant.SkillFailed));
    }
}

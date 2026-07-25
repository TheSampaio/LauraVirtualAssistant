using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Adjusts the system master volume.
/// </summary>
public sealed class VolumeSkill : ISkill
{
    private const int StepCount = 4;

    private readonly ILocalizer _localizer;
    private readonly ISystemController _systemController;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and the response.
    ///     systemController: Service that changes the system volume.
    /// </summary>
    public VolumeSkill(ILocalizer localizer, ISystemController systemController)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(systemController);

        _localizer = localizer;
        _systemController = systemController;
    }

    /// <inheritdoc />
    public string Id => "builtin.volume";

    /// <inheritdoc />
    public int Priority => 30;

    /// <inheritdoc />
    public bool CanHandle(SkillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAction(request.NormalizedText) is not VolumeAction.None;
    }

    /// <inheritdoc />
    public Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (ResolveAction(request.NormalizedText))
        {
            case VolumeAction.Increase:
                _systemController.AdjustVolume(StepCount);
                break;

            case VolumeAction.Decrease:
                _systemController.AdjustVolume(-StepCount);
                break;

            case VolumeAction.ToggleMute:
                _systemController.ToggleMute();
                break;

            default:
                return Task.FromResult(SkillResponse.NotHandled);
        }

        return Task.FromResult(SkillResponse.Speak(_localizer.Get(LocalizationKeys.Skills.VolumeResponse)));
    }

    /// <summary>
    /// Decides which volume adjustment the command is asking for.
    ///
    /// Args:
    ///     normalizedText: The already-normalized command.
    ///
    /// Returns:
    ///     The matching adjustment, or <see cref="VolumeAction.None"/> when the
    ///     command is not about volume.
    /// </summary>
    private VolumeAction ResolveAction(string normalizedText)
    {
        if (PhraseMatcher.MatchesAny(normalizedText, _localizer.GetPhrases(LocalizationKeys.Skills.VolumeUpPhrases)))
        {
            return VolumeAction.Increase;
        }

        if (PhraseMatcher.MatchesAny(normalizedText, _localizer.GetPhrases(LocalizationKeys.Skills.VolumeDownPhrases)))
        {
            return VolumeAction.Decrease;
        }

        return PhraseMatcher.MatchesAny(normalizedText, _localizer.GetPhrases(LocalizationKeys.Skills.VolumeMutePhrases))
            ? VolumeAction.ToggleMute
            : VolumeAction.None;
    }

    /// <summary>
    /// Volume adjustments the skill recognizes.
    /// </summary>
    private enum VolumeAction
    {
        /// <summary>The command is not about volume.</summary>
        None,

        /// <summary>Raise the volume.</summary>
        Increase,

        /// <summary>Lower the volume.</summary>
        Decrease,

        /// <summary>Toggle mute.</summary>
        ToggleMute,
    }
}

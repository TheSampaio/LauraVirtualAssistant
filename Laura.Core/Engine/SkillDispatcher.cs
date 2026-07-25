using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Chooses and runs the skill that handles each command.
///
/// The query order is fixed by <see cref="ISkill.Priority"/> at
/// construction time. If no skill accepts the command and generative mode is
/// enabled, the request is forwarded to the model - this is the only system point that
/// knows generative mode exists.
/// </summary>
public sealed class SkillDispatcher : ISkillDispatcher
{
    private readonly IReadOnlyList<ISkill> _skills;
    private readonly ILocalizer _localizer;
    private readonly IConversationEngine _conversationEngine;
    private readonly ISettingsService _settings;
    private readonly IUserContext _userContext;
    private readonly ILogger<SkillDispatcher> _logger;

    /// <summary>
    /// Initializes the dispatcher by ordering the registered skills.
    ///
    /// Args:
    ///     skills: Available skills, in any order.
    ///     localizer: Source of error and refusal messages.
    ///     conversationEngine: Optional generative engine.
    ///     settings: Current settings, consulted to know whether generative mode
    ///     is enabled.
    ///     userContext: User context passed to the generative engine.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public SkillDispatcher(
        IEnumerable<ISkill> skills,
        ILocalizer localizer,
        IConversationEngine conversationEngine,
        ISettingsService settings,
        IUserContext userContext,
        ILogger<SkillDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(conversationEngine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(userContext);
        ArgumentNullException.ThrowIfNull(logger);

        _skills = [.. skills.OrderBy(static skill => skill.Priority).ThenBy(static skill => skill.Id, StringComparer.Ordinal)];
        _localizer = localizer;
        _conversationEngine = conversationEngine;
        _settings = settings;
        _userContext = userContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (ISkill skill in _skills)
        {
            if (!skill.CanHandle(request))
            {
                continue;
            }

            SkillResponse response = await ExecuteSafelyAsync(skill, request, cancellationToken).ConfigureAwait(false);

            if (response.Handled)
            {
                _logger.LogInformation("Command \"{Command}\" handled by {Skill}.", request.RawText, skill.Id);
                return response;
            }
        }

        return await AskConversationEngineAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _settings.Current.GenerativeAi.Enabled
            && _conversationEngine.IsAvailable
            && !_skills.Any(skill => skill.CanHandle(request));
    }

    /// <summary>
    /// Runs a skill while isolating its failures.
    ///
    /// A skill that throws cannot bring down the whole assistant; the error becomes
    /// a spoken response and the log keeps the details.
    ///
    /// Args:
    ///     skill: Skill to run.
    ///     request: Command to handle.
    ///     cancellationToken: Token that aborts execution.
    ///
    /// Returns:
    ///     The skill response, or a generic failure response.
    /// </summary>
    private async Task<SkillResponse> ExecuteSafelyAsync(
        ISkill skill,
        SkillRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await skill.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Skill {Skill} failed while handling \"{Command}\".", skill.Id, request.RawText);
            return SkillResponse.Speak(_localizer.Get(LocalizationKeys.Assistant.SkillFailed));
        }
    }

    /// <summary>
    /// Forwards to the generative model a command no skill recognized.
    ///
    /// Args:
    ///     request: Unrecognized command.
    ///     cancellationToken: Token that aborts the request.
    ///
    /// Returns:
    ///     The model response, or <see cref="SkillResponse.NotHandled"/> when
    ///     generative mode is off, unavailable, or did not answer.
    /// </summary>
    private async Task<SkillResponse> AskConversationEngineAsync(
        SkillRequest request,
        CancellationToken cancellationToken)
    {
        if (!_settings.Current.GenerativeAi.Enabled || !_conversationEngine.IsAvailable)
        {
            _logger.LogInformation("No skill recognized \"{Command}\".", request.RawText);
            return SkillResponse.NotHandled;
        }

        var turn = new ConversationTurn(request.RawText, request.Culture, _userContext.DisplayName);
        string? answer = await _conversationEngine.AskAsync(turn, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(answer)
            ? SkillResponse.NotHandled
            : SkillResponse.Speak(answer);
    }
}

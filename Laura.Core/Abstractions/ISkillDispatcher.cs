using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>
/// Routes a command to the appropriate skill.
/// </summary>
public interface ISkillDispatcher
{
    /// <summary>
    /// Finds the skill able to handle the command and runs it.
    ///
    /// Args:
    ///     request: Command received.
    ///     cancellationToken: Token that aborts dispatch.
    ///
    /// Returns:
    ///     The chosen skill response, or <see cref="SkillResponse.NotHandled"/>
    ///     when none recognizes the command.
    /// </summary>
    Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether dispatch would fall through to the generative engine.
    ///
    /// Args:
    ///     request: Command received.
    ///
    /// Returns:
    ///     <see langword="true"/> when no local skill accepts the command and the
    ///     generative engine is configured.
    /// </summary>
    bool CanUseGenerativeFallback(SkillRequest request);
}

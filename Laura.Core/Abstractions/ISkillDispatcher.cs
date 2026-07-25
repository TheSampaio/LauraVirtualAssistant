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
    ///     request: Comando recebido.
    ///     cancellationToken: Token que aborta o despacho.
    ///
    /// Returns:
    ///     The chosen skill response, or <see cref="SkillResponse.NotHandled"/>
    ///     when none recognizes the command.
    /// </summary>
    Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default);
}

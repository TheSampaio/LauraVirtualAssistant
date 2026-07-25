using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>
/// Uma capacidade isolada de Laura, como dizer as horas ou open um aplicativo.
///
/// Each skill decides on its own whether a command belongs to it and only executes that;
/// adding a new capability means registering one more implementation, without touching
/// no motor nem nas habilidades existentes.
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Gets the stable skill identifier, used in diagnostic logs.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the evaluation order; lower values are queried first.
    ///
    /// Skills with specific triggers must precede broad-trigger skills,
    /// so "open settings" is not captured by "open".
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Reports whether this skill recognizes the command.
    ///
    /// Must be cheap and free of side effects: it is called for each
    /// registered skill until one accepts.
    ///
    /// Args:
    ///     request: Command to evaluate.
    ///
    /// Returns:
    ///     <see langword="true"/> when the skill knows how to handle the command.
    /// </summary>
    bool CanHandle(SkillRequest request);

    /// <summary>
    /// Executes the command.
    ///
    /// Args:
    ///     request: Command to handle, already approved by <see cref="CanHandle"/>.
    ///     cancellationToken: Token that aborts execution.
    ///
    /// Returns:
    ///     A answer a falar, ou <see cref="SkillResponse.NotHandled"/> quando a
    ///     skill gives up after inspecting the command more deeply.
    /// </summary>
    Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default);
}

using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>
/// An isolated Laura capability, such as telling the time or opening an application.
///
/// Each skill decides on its own whether a command belongs to it and only executes that;
/// adding a new capability means registering one more implementation, without touching
/// the engine or the existing skills.
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
    ///     An answer to speak, or <see cref="SkillResponse.NotHandled"/> when the
    ///     skill gives up after inspecting the command more deeply.
    /// </summary>
    Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default);
}

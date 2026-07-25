namespace Laura.Core.Skills;

/// <summary>
/// The result of running a skill.
/// </summary>
public sealed record SkillResponse
{
    /// <summary>
    /// Response indicating the skill did not handle the command, letting the
    /// dispatcher move on to the next candidate.
    /// </summary>
    public static SkillResponse NotHandled { get; } = new() { Handled = false };

    /// <summary>Gets a value indicating whether the command was handled.</summary>
    public bool Handled { get; init; } = true;

    /// <summary>
    /// Gets the text to be spoken, or <see langword="null"/> when the skill acts
    /// silently.
    /// </summary>
    public string? SpokenText { get; init; }

    /// <summary>
    /// Gets a value indicating whether the application should quit after the response.
    /// </summary>
    public bool RequestsShutdown { get; init; }

    /// <summary>
    /// Creates a spoken response.
    ///
    /// Args:
    ///     text: Text Laura will say.
    ///
    /// Returns:
    ///     A handled response that produces speech.
    /// </summary>
    public static SkillResponse Speak(string text) => new() { SpokenText = text };

    /// <summary>
    /// Creates a handled response that produces no speech.
    ///
    /// Returns:
    ///     A handled, silent response.
    /// </summary>
    public static SkillResponse Silent() => new();

    /// <summary>
    /// Creates a farewell response that quits the application after speaking.
    ///
    /// Args:
    ///     text: Farewell text.
    ///
    /// Returns:
    ///     A handled response that speaks and then requests shutdown.
    /// </summary>
    public static SkillResponse Farewell(string text) => new()
    {
        SpokenText = text,
        RequestsShutdown = true,
    };
}

using Laura.Core.Abstractions;
using Laura.Core.Skills;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test skill with configurable trigger and response.
/// </summary>
public sealed class FakeSkill : ISkill
{
    private readonly string _trigger;
    private readonly SkillResponse _response;

    /// <summary>
    /// Creates the test skill.
    ///
    /// Args:
    ///     id: Skill identifier.
    ///     priority: Evaluation priority.
    ///     trigger: Normalized text recognized by the skill.
    ///     response: Response returned when the trigger matches.
    /// </summary>
    public FakeSkill(string id, int priority, string trigger, SkillResponse response)
    {
        Id = id;
        Priority = priority;
        _trigger = trigger;
        _response = response;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public int Priority { get; }

    /// <summary>Gets how many times the skill was executed.</summary>
    public int ExecutionCount { get; private set; }

    /// <inheritdoc />
    public bool CanHandle(SkillRequest request) =>
        request.NormalizedText.Contains(_trigger, StringComparison.Ordinal);

    /// <inheritdoc />
    public Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return Task.FromResult(_response);
    }
}

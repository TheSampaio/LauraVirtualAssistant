using Laura.Core.Abstractions;
using Laura.Core.Skills;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test skill that always throws, to verify failure isolation.
/// </summary>
public sealed class FailingSkill : ISkill
{
    /// <inheritdoc />
    public string Id => "failing";

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanHandle(SkillRequest request) => true;

    /// <inheritdoc />
    public Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Falha proposital.");
}

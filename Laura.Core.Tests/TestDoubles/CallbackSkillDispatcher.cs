using Laura.Core.Abstractions;
using Laura.Core.Skills;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Delegates dispatch to a test callback so request cancellation can be observed directly.</summary>
public sealed class CallbackSkillDispatcher(Func<SkillRequest, CancellationToken, Task<SkillResponse>> callback) : ISkillDispatcher
{
    /// <inheritdoc />
    public Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default) => callback(request, cancellationToken);
    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request) => true;
}

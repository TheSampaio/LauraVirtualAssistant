using Laura.Core.Engine;

namespace Laura.Core.Abstractions;

/// <summary>Persists bounded task checkpoints outside the shared Brain; never persists execution authority.</summary>
public interface ICognitiveTaskJournal
{
    /// <summary>Atomically commits a checkpoint before effects or after observed results.</summary>
    Task SaveAsync(CognitiveTaskCheckpoint checkpoint, CancellationToken cancellationToken = default);
    /// <summary>Restores one checkpoint without executing any recorded operation.</summary>
    Task<CognitiveTaskCheckpoint?> ReadAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Lists the most recently updated private checkpoints within a fixed retention bound.</summary>
    Task<IReadOnlyList<CognitiveTaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Removes one private checkpoint after a trusted native user action, never a model tool.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

using Laura.Core.Brain;

namespace Laura.Core.Abstractions;

/// <summary>Persists private acquisition progress outside the Brain shared root and rebuildable indexes.</summary>
public interface IRepositoryStudyStore
{
    /// <summary>Commits one inventory/progress snapshot atomically.</summary>
    Task SaveAsync(RepositoryStudyState state, CancellationToken cancellationToken = default);
    /// <summary>Restores operational progress without treating it as source knowledge or authority.</summary>
    Task<RepositoryStudyState?> ReadAsync(Guid id, CancellationToken cancellationToken = default);
}

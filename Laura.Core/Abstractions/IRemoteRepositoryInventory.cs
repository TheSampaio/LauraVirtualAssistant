using Laura.Core.Brain;

namespace Laura.Core.Abstractions;

/// <summary>Reads a public repository's frozen tree incrementally without trusting a truncated recursive response as full coverage.</summary>
public interface IRemoteRepositoryInventory
{
    /// <summary>Resolves repository metadata and one immutable revision, leaving tree traversal as durable pending work.</summary>
    Task<RemoteRepositoryInventoryPage> BeginInventoryAsync(GitHubRepositoryAddress address, RemoteBrainBudget budget, CancellationToken cancellationToken);
    /// <summary>Reads exactly one nonrecursive frozen subtree under a per-operation HTTP/byte budget.</summary>
    Task<RemoteRepositoryInventoryPage> ReadInventoryPageAsync(RemoteBrainRepository repository, RemoteRepositoryTree tree, RemoteBrainBudget budget, CancellationToken cancellationToken);
}

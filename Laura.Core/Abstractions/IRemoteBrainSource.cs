using Laura.Core.Brain;

namespace Laura.Core.Abstractions;

/// <summary>Reads bounded untrusted repository data, never installs or executes it.</summary>
public interface IRemoteBrainSource
{
    /// <summary>Resolves an immutable revision and inventories its ordinary files within a shared request budget.</summary>
    Task<RemoteBrainRepository> InspectAsync(GitHubRepositoryAddress address, RemoteBrainBudget budget, CancellationToken cancellationToken);
    /// <summary>Reads and verifies one inventoried blob within the same acquisition budget.</summary>
    Task<byte[]> ReadAsync(RemoteBrainRepository repository, RemoteBrainFile file, RemoteBrainBudget budget, CancellationToken cancellationToken);
}

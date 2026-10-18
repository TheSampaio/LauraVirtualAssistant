namespace Laura.Core.Brain;

/// <summary>One frozen nonrecursive inventory page, with explicit remaining subtrees and actual API truncation.</summary>
public sealed record RemoteRepositoryInventoryPage(RemoteBrainRepository Repository, IReadOnlyList<RemoteRepositoryTree> PendingTrees, int ExcludedEntries);

/// <summary>A directory prefix and immutable tree identity; neither value is a filesystem path or an execution instruction.</summary>
public sealed record RemoteRepositoryTree(string Path, string Hash);

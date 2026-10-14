namespace Laura.Core.Brain;

/// <summary>Untrusted repository metadata tied to a resolved immutable commit.</summary>
public sealed record RemoteBrainRepository(GitHubRepositoryAddress Address, string Revision, string License,
    string Attribution, IReadOnlyList<RemoteBrainFile> Files, bool Truncated);

/// <summary>An ordinary Git blob; its repository path is metadata and never a local filesystem destination.</summary>
public sealed record RemoteBrainFile(string Path, string BlobHash, int Size);

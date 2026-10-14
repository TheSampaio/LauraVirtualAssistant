namespace Laura.Core.Brain;

/// <summary>An ordinary Git blob; its repository path is metadata and never a local filesystem destination.</summary>
public sealed record RemoteBrainFile(string Path, string BlobHash, int Size);

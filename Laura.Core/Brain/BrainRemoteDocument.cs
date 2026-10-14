namespace Laura.Core.Brain;

/// <summary>Links one actually retained source file to its current Markdown version and original Git/SHA-256 byte identities.</summary>
public sealed record BrainRemoteDocument(string Path, string Id, string Hash, string ByteHash, string BlobHash);

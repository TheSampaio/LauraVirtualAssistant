namespace Laura.Core.Brain;

/// <summary>Reports observed acquisition coverage and evidence; never claims comprehension, activation or execution.</summary>
public sealed record BrainRemoteAcquisitionResult(string NoteId, string Repository, string Revision, string License, string State,
    int InventoryFiles, bool InventoryTruncated, int Imported, int Reused, int Excluded, IReadOnlyList<BrainRemoteDocument> Documents,
    IReadOnlyList<string> Languages, IReadOnlyList<string> DeclaredDependencies, IReadOnlyList<string> SuggestedPaths,
    int HttpRequests, int ReceivedBytes, long ElapsedMilliseconds, string Failure);

/// <summary>Links one actually retained source file to its current Markdown version and original Git/SHA-256 byte identities.</summary>
public sealed record BrainRemoteDocument(string Path, string Id, string Hash, string ByteHash, string BlobHash);

using Laura.Core.Files;

namespace Laura.Core.Abstractions;

/// <summary>Performs version-checked file effects after the caller's existing native permission review.</summary>
public interface IVersionedFileEditor
{
    /// <summary>Reads bounded text and the hash of the complete file. Args: path: Authorized local path; token: Cancellation. Returns: Versioned snapshot.</summary>
    Task<VersionedFileSnapshot> ReadAsync(string path, CancellationToken token = default);
    /// <summary>Creates a new file without overwriting existing content. Returns: Verified durable receipt.</summary>
    Task<FileEditReceipt> CreateAsync(string path, string content, CancellationToken token = default);
    /// <summary>Replaces exactly one literal passage in the expected version, preserving encoding and unrelated bytes. Returns: Verified receipt.</summary>
    Task<FileEditReceipt> PatchAsync(string path, string expectedHash, string oldText, string newText, CancellationToken token = default);
    /// <summary>Restores the exact prior bytes only while the edited version still matches. Returns: Updated durable receipt.</summary>
    Task<FileEditReceipt> RollbackAsync(Guid id, string path, CancellationToken token = default);
}

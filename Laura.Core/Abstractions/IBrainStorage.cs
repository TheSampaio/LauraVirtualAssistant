namespace Laura.Core.Abstractions;

/// <summary>Portable document storage boundary; a mobile document-provider adapter need not expose real filesystem paths.</summary>
public interface IBrainStorage
{
    /// <summary>Validates an identifier before a multi-file operation mutates source metadata.</summary>
    void ValidatePath(string path);
    /// <summary>Gets an opaque storage identity, not a note-relative link.</summary>
    string Location { get; }
    /// <summary>Lists portable relative file identifiers.</summary>
    IReadOnlyList<string> EnumerateFiles();
    /// <summary>Lists actual directories, including empty folders created by the user.</summary>
    IReadOnlyList<string> EnumerateDirectories();
    /// <summary>Reads a bounded document.</summary>
    string Read(string path);
    /// <summary>Reads a bounded attachment without changing binary bytes.</summary>
    byte[] ReadBytes(string path);
    /// <summary>Copies a new attachment without overwriting existing content.</summary>
    void WriteBytes(string path, byte[] content);
    /// <summary>Writes atomically where supported, requiring the caller's last observed content hash.</summary>
    void Write(string path, string content, string? expectedHash);
    /// <summary>Moves a document without overwriting another document.</summary>
    void Move(string from, string to);
    /// <summary>Creates a user-requested directory.</summary>
    void CreateDirectory(string path);
}

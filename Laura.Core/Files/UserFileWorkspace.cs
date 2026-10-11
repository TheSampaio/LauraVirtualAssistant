using System.Security.Cryptography;
using System.Text;

namespace Laura.Core.Files;

/// <summary>Creates only generated notes/screenshots in a dedicated folder, with guarded single-operation undo.</summary>
public sealed class UserFileWorkspace
{
    private readonly string _directory;
    private readonly object _gate = new();
    private string? _lastPath;
    private byte[]? _lastHash;

    /// <summary>Targets an app-owned user folder without creating it until needed.</summary>
    public UserFileWorkspace(string directory) => _directory = Path.GetFullPath(directory);

    /// <summary>Lists at most 100 direct workspace files, excluding links.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        ValidateDirectory();
        return Directory.Exists(_directory) ? [.. Directory.EnumerateFiles(_directory)
            .Where(static path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Take(100).Select(Path.GetFileName).OfType<string>()] : [];
    }

    /// <summary>Writes a new UTF-8 note without using any user/model text as a path.</summary>
    public string SaveNote(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 16000) { throw new ArgumentException("Note exceeds 16000 characters.", nameof(text)); }
        return SaveArtifact(".txt", Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Saves only a bounded note or PNG under an unpredictable new filename; never overwrites existing files.</summary>
    public string SaveArtifact(string extension, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (extension is not (".txt" or ".png") || bytes.Length > 32 * 1024 * 1024)
        {
            throw new ArgumentException("Unsupported workspace artifact.", nameof(extension));
        }
        lock (_gate)
        {
            ValidateDirectory();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "laura-" + Guid.NewGuid().ToString("N") + extension);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); }
            _lastHash = SHA256.HashData(bytes);
            _lastPath = path;
            return path;
        }
    }

    /// <summary>Deletes only the last artifact created this session, refusing changed or linked files.</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            ValidateDirectory();
            if (_lastPath is null || !File.Exists(_lastPath)) { return false; }
            if ((File.GetAttributes(_lastPath) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Cannot undo a linked file."); }
            byte[] hash;
            using (var file = new FileStream(_lastPath, FileMode.Open, FileAccess.Read, FileShare.Read)) { hash = SHA256.HashData(file); }
            if (!hash.AsSpan().SequenceEqual(_lastHash)) { throw new IOException("The file was edited after creation. Undo refused."); }
            File.Delete(_lastPath);
            _lastPath = null;
            _lastHash = null;
            return true;
        }
    }

    /// <summary>Rejects junctions and symlinks in the workspace ancestry before writing or deleting.</summary>
    private void ValidateDirectory()
    {
        for (DirectoryInfo? directory = new(_directory); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Linked file workspaces are not supported.");
            }
        }
    }
}

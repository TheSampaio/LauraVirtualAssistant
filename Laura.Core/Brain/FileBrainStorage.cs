using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Laura.Core.Abstractions;

namespace Laura.Core.Brain;

/// <summary>Bounded, symlink-safe local storage with optimistic writes and per-vault writer exclusion.</summary>
public sealed class FileBrainStorage : IBrainStorage
{
    /// <summary>Validates the selected root and protects a vault placed inside a Git worktree before writing personal data.</summary>
    public FileBrainStorage(string location)
    {
        Location = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
        if (Path.GetPathRoot(Location) == Location || HasRepositoryMetadata(Location)) { throw new IOException("Select a dedicated vault directory outside a repository root."); }
        CheckAncestors(Location); ProtectGit(Location);
        Directory.CreateDirectory(Location);
    }
    /// <inheritdoc />
    public string Location { get; }
    /// <inheritdoc />
    public void ValidatePath(string path) => _ = Resolve(path);
    /// <summary>Calculates an edit identity from exact UTF-8 text.</summary>
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    /// <summary>Resolves only relative identifiers that remain within the selected root and have no link ancestors.</summary>
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal)
            || path.Split('/', '\\').Any(part => part is ".." or "." || part.Length == 0)) { throw new IOException("Invalid vault-relative path."); }
        string full = Path.GetFullPath(Path.Combine(Location, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Location + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new IOException("Path leaves the selected vault."); }
        CheckAncestors(full);
        for (string? item = full; item is not null && !item.Equals(Location, StringComparison.OrdinalIgnoreCase); item = Path.GetDirectoryName(item))
        { if (Path.GetFileName(item) == ".git" || HasRepositoryMetadata(item)) { throw new IOException("Repository contents cannot be used as vault documents."); } }
        return full;
    }
    /// <summary>Identifies repository and worktree roots before importing or rewriting their documents.</summary>
    private static bool HasRepositoryMetadata(string path) => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));
    /// <summary>Rejects redirecting links rather than following documents outside the authorized root.</summary>
    private static void CheckAncestors(string path)
    {
        for (string? item = path; item is not null; item = Path.GetDirectoryName(item))
        {
            if ((File.Exists(item) || Directory.Exists(item)) && IsLink(item))
            { throw new IOException("Linked vault paths are not supported by this storage adapter."); }
        }
    }
    /// <summary>Distinguishes redirecting links from ordinary cloud-file placeholders.</summary>
    private static bool IsLink(string path) => Directory.Exists(path) ? new DirectoryInfo(path).LinkTarget is not null : new FileInfo(path).LinkTarget is not null;
    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles()
    {
        var files = new List<string>(); var pending = new Stack<string>(); pending.Push(Location);
        while (pending.TryPop(out string? directory))
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(directory))
            {
                if (IsLink(child)) { continue; }
                if (Directory.Exists(child)) { if (Path.GetFileName(child) != ".git" && !HasRepositoryMetadata(child)) { pending.Push(child); } }
                else if (!Path.GetFileName(child).StartsWith('.') && !child.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                { files.Add(Path.GetRelativePath(Location, child).Replace('\\', '/')); }
                if (files.Count > 50000) { throw new IOException("Vault exceeds the supported file inventory limit."); }
            }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateDirectories()
    {
        var directories = new List<string>(); var pending = new Stack<string>(); pending.Push(Location);
        while (pending.TryPop(out string? parent))
        {
            foreach (string directory in Directory.EnumerateDirectories(parent))
            { if (IsLink(directory) || Path.GetFileName(directory) == ".git" || HasRepositoryMetadata(directory)) { continue; } directories.Add(Path.GetRelativePath(Location, directory).Replace('\\', '/')); pending.Push(directory); }
            if (directories.Count > 50000) { throw new IOException("Vault directory limit exceeded."); }
        }
        return directories;
    }
    /// <inheritdoc />
    public string Read(string path)
    {
        string full = Resolve(path);
        if (new FileInfo(full).Length > 1048576) { throw new IOException("Document exceeds the one-MiB limit."); }
        return File.ReadAllText(full, Encoding.UTF8);
    }
    /// <inheritdoc />
    public byte[] ReadBytes(string path)
    {
        string full = Resolve(path);
        if (new FileInfo(full).Length > 67108864) { throw new IOException("Attachment exceeds the 64-MiB transfer limit."); }
        return File.ReadAllBytes(full);
    }
    /// <inheritdoc />
    public void WriteBytes(string path, byte[] content)
    {
        ProtectGit(Location);
        if (content.Length > 67108864) { throw new IOException("Attachment exceeds the 64-MiB transfer limit."); }
        string full = Resolve(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, content); File.Move(temp, full, false); } finally { if (File.Exists(temp)) { File.Delete(temp); } }
    }
    /// <inheritdoc />
    public void Write(string path, string content, string? expectedHash)
    {
        ProtectGit(Location);
        if (Encoding.UTF8.GetByteCount(content) > 1048576) { throw new IOException("Document exceeds the one-MiB limit."); }
        string full = Resolve(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var writerLock = new FileStream(Path.Combine(Location, ".laura-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        bool exists = File.Exists(full);
        if (expectedHash is null ? exists : !exists || Hash(File.ReadAllText(full, Encoding.UTF8)) != expectedHash) { throw new BrainConflictException(); }
        string temp = Path.Combine(Path.GetDirectoryName(full)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] bytes = new UTF8Encoding(false).GetBytes(content); stream.Write(bytes); stream.Flush(true); }
            if (exists && Hash(File.ReadAllText(full, Encoding.UTF8)) != expectedHash) { throw new BrainConflictException(); }
            File.Move(temp, full, exists);
        }
        finally { if (File.Exists(temp)) { File.Delete(temp); } }
    }
    /// <inheritdoc />
    public void Move(string from, string to)
    {
        string source = Resolve(from), destination = Resolve(to);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) || Directory.Exists(destination)) { throw new IOException("The destination already exists."); }
        if (Directory.Exists(source)) { Directory.Move(source, destination); } else { File.Move(source, destination, false); }
    }
    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(Resolve(path));
    /// <summary>Adds a vault-specific ignore rule and rejects already tracked personal paths without removing local files or rewriting history.</summary>
    private static void ProtectGit(string location)
    {
        string? root = Path.GetDirectoryName(location);
        while (root is not null && !Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git"))) { root = Path.GetDirectoryName(root); }
        if (root is null) { return; }
        string relative = Path.GetRelativePath(root, location).Replace('\\', '/');
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "ls-files", "-z", "--", ":(icase,literal)" + relative + "/" }) { start.ArgumentList.Add(argument); }
        try
        {
            using Process process = Process.Start(start) ?? throw new IOException("Git safety verification failed.");
            string tracked = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000) || process.ExitCode != 0 || tracked.Length > 0) { throw new IOException("The selected vault has tracked files or Git safety cannot be verified. Keep originals and untrack them explicitly before continuing."); }
        }
        catch (System.ComponentModel.Win32Exception) { throw new IOException("Git safety verification is unavailable for this location."); }
        start.ArgumentList.Clear(); foreach (string argument in new[] { "check-ignore", "-q", "--", relative + "/" }) { start.ArgumentList.Add(argument); }
        using (Process ignored = Process.Start(start) ?? throw new IOException("Git ignore verification failed."))
        { _ = ignored.StandardOutput.ReadToEnd(); _ = ignored.StandardError.ReadToEnd(); ignored.WaitForExit(); if (ignored.ExitCode == 0) { return; } if (ignored.ExitCode != 1) { throw new IOException("Git ignore verification failed."); } }
        if (relative.Contains('\n', StringComparison.Ordinal) || relative.Contains('\r', StringComparison.Ordinal)) { throw new IOException("Invalid Git vault location."); }
        string rule = "/" + string.Concat(relative.Select(character => "[]*?!#".Contains(character, StringComparison.Ordinal) ? "\\" + character : character.ToString())) + "/";
        string ignore = Path.Combine(root, ".gitignore"); string old = File.Exists(ignore) ? File.ReadAllText(ignore) : "";
        if (!old.Split('\n').Any(line => line.TrimEnd('\r') == rule)) { File.AppendAllText(ignore, "\n# Private LAURA Brain location\n" + rule + "\n", new UTF8Encoding(false)); }
    }
}

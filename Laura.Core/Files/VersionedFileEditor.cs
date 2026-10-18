using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Laura.Core.Abstractions;

namespace Laura.Core.Files;

/// <summary>A bounded text view with the complete source version, independent of its displayed truncation.</summary>
public sealed record VersionedFileSnapshot(string Path, string Hash, string Content, long Bytes, bool Truncated);

/// <summary>Durable effect evidence; backups remain in protected assistant storage, outside the Brain.</summary>
public sealed record FileEditReceipt(Guid Id, string Path, string? BeforeHash, string AfterHash, string State, int RemovedCharacters, int AddedCharacters);

/// <summary>Provides closed native failure metadata; messages originate in the adapter, never a process or model.</summary>
public sealed class FileEditException(string code, string message, bool effectUncertain = false) : IOException(message)
{
    /// <summary>Gets the native recovery category.</summary>
    public string Code { get; } = code;
    /// <summary>Gets whether a replacement may already have occurred and requires version inspection.</summary>
    public bool EffectUncertain { get; } = effectUncertain;
}

/// <summary>Serializes native edits, detects stale versions, swaps files atomically and retains guarded rollback receipts.</summary>
public sealed class VersionedFileEditor(string directory) : IVersionedFileEditor, IDisposable
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<VersionedFileSnapshot> ReadAsync(string path, CancellationToken token = default)
    {
        byte[] bytes = await ReadBytesAsync(path, token).ConfigureAwait(false);
        var (text, _) = Decode(bytes);
        return new(Path.GetFullPath(path), Hash(bytes), text[..Math.Min(text.Length, 32000)], bytes.Length, text.Length > 32000);
    }

    /// <inheritdoc />
    public async Task<FileEditReceipt> CreateAsync(string path, string content, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateHtml(path, content);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            path = SafePath(path);
            if (File.Exists(path)) { throw new FileEditException("existing_file", "The file exists. Read its hash and use a scoped patch instead of replacing the whole file."); }
            return await CommitAsync(path, null, new UTF8Encoding(false, true).GetBytes(content), 0, content.Length, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<FileEditReceipt> PatchAsync(string path, string expectedHash, string oldText, string newText, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldText); ArgumentNullException.ThrowIfNull(newText);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            path = SafePath(path);
            byte[] before = await ReadBytesAsync(path, token).ConfigureAwait(false);
            RequireHash(before, expectedHash);
            var (text, encoding) = Decode(before);
            int start = text.IndexOf(oldText, StringComparison.Ordinal);
            if (start < 0 || text.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0)
            { throw new FileEditException("ambiguous_patch", "The patch passage must match exactly once in the expected file version."); }
            if (oldText == newText) { throw new FileEditException("no_change", "The patch makes no change."); }
            string updated = text[..start] + newText + text[(start + oldText.Length)..];
            ValidateHtml(path, updated);
            byte[] payload = encoding.GetPreamble().Concat(encoding.GetBytes(updated)).ToArray();
            return await CommitAsync(path, before, payload, oldText.Length, newText.Length, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<FileEditReceipt> RollbackAsync(Guid id, string path, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            path = SafePath(path);
            FileEditReceipt receipt = JsonSerializer.Deserialize<FileEditReceipt>(await File.ReadAllTextAsync(ReceiptPath(id), token).ConfigureAwait(false))
                ?? throw new IOException("The edit receipt is invalid.");
            if (receipt.Id != id || !string.Equals(receipt.Path, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            { throw new IOException("The rollback receipt does not match the authorized path."); }
            if (receipt.State == "RolledBack") { return receipt; }
            if (receipt.State != "Committed") { throw new FileEditException("uncertain_edit", "The edit is uncertain. Inspect the preserved versions before recovery.", true); }
            RequireHash(await ReadBytesAsync(path, token).ConfigureAwait(false), receipt.AfterHash);
            if (receipt.BeforeHash is null) { File.Delete(path); }
            else
            {
                byte[] before = await File.ReadAllBytesAsync(BackupPath(id), token).ConfigureAwait(false);
                RequireHash(before, receipt.BeforeHash);
                await WriteReceiptAsync(receipt with { State = "RollbackPrepared" }, token).ConfigureAwait(false);
                await SwapAsync(path, before, receipt.AfterHash, BackupPath(id) + ".rollback-replaced", token).ConfigureAwait(false);
                RequireHash(await File.ReadAllBytesAsync(BackupPath(id) + ".rollback-replaced", CancellationToken.None).ConfigureAwait(false), receipt.AfterHash, true);
                RequireHash(await ReadBytesAsync(path, CancellationToken.None).ConfigureAwait(false), receipt.BeforeHash, true);
            }
            receipt = receipt with { State = "RolledBack" };
            await WriteReceiptAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            return receipt;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Persists intent before the mutation and commits only after byte-level verification.</summary>
    private async Task<FileEditReceipt> CommitAsync(string path, byte[]? before, byte[] after, int removed, int added, CancellationToken token)
    {
        if (after.Length > MaximumBytes) { throw new IOException("The edit exceeds the reversible file limit."); }
        Directory.CreateDirectory(_directory);
        var receipt = new FileEditReceipt(Guid.NewGuid(), path, before is null ? null : Hash(before), Hash(after), "Prepared", removed, added);
        if (before is not null) { await File.WriteAllBytesAsync(BackupPath(receipt.Id), before, token).ConfigureAwait(false); }
        await WriteReceiptAsync(receipt, token).ConfigureAwait(false);
        await SwapAsync(path, after, receipt.BeforeHash, before is null ? null : BackupPath(receipt.Id) + ".replaced", token).ConfigureAwait(false);
        if (before is not null)
        {
            // File.Replace preserves the actual replaced version, including a concurrent external rename.
            byte[] replaced = await File.ReadAllBytesAsync(BackupPath(receipt.Id) + ".replaced", CancellationToken.None).ConfigureAwait(false);
            if (Hash(replaced) != receipt.BeforeHash)
            {
                await WriteReceiptAsync(receipt with { State = "Conflict" }, CancellationToken.None).ConfigureAwait(false);
                throw new FileEditException("external_replacement_conflict", "An external replacement raced with the edit. Both versions were preserved; inspect the conflict before continuing.", true);
            }
        }
        RequireHash(await ReadBytesAsync(path, CancellationToken.None).ConfigureAwait(false), receipt.AfterHash, true);
        receipt = receipt with { State = "Committed" };
        await WriteReceiptAsync(receipt, CancellationToken.None).ConfigureAwait(false);
        return receipt;
    }

    /// <summary>Flushes a sibling temporary file before a same-volume atomic swap; checks the preimage immediately before the effect.</summary>
    private static async Task SwapAsync(string path, byte[] bytes, string? expectedHash, string? replacedBackup, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".laura-" + Guid.NewGuid().ToString("N") + ".tmp";
        string? localReplaced = replacedBackup is null ? null : path + ".laura-" + Path.GetFileName(replacedBackup);
        bool attempted = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            { await stream.WriteAsync(bytes, token).ConfigureAwait(false); stream.Flush(true); }
            token.ThrowIfCancellationRequested();
            _ = SafePath(path);
            if (expectedHash is null) { File.Move(temporary, path, false); }
            else
            {
                RequireHash(await ReadBytesAsync(path, token).ConfigureAwait(false), expectedHash);
                attempted = true;
                // Windows requires the atomic replacement's backup on the source volume.
                File.Replace(temporary, path, localReplaced);
                if (localReplaced is not null && replacedBackup is not null)
                {
                    await using var original = new FileStream(localReplaced, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var backup = new FileStream(replacedBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
                    await original.CopyToAsync(backup, CancellationToken.None).ConfigureAwait(false); backup.Flush(true);
                }
            }
        }
        catch (Exception error) when (attempted && error is IOException or UnauthorizedAccessException)
        { throw new FileEditException("native_commit_uncertain", "A native file replacement or backup failed. Inspect the durable receipt and preserved versions before retrying.", true); }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        if (localReplaced is not null && File.Exists(localReplaced)) { File.Delete(localReplaced); }
    }

    /// <summary>Writes receipt transitions atomically so restart never depends on an in-memory undo stack.</summary>
    private async Task WriteReceiptAsync(FileEditReceipt receipt, CancellationToken token)
    {
        string target = ReceiptPath(receipt.Id), temporary = target + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        { await JsonSerializer.SerializeAsync(stream, receipt, cancellationToken: token).ConfigureAwait(false); stream.Flush(true); }
        File.Move(temporary, target, true);
    }

    /// <summary>Rejects links before reading a bounded local file.</summary>
    private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken token)
    {
        path = SafePath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaximumBytes) { throw new IOException("The text file exceeds the reversible file limit."); }
        byte[] bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
    }

    /// <summary>Preserves strict UTF-8 or BOM-marked UTF-16 encoding rather than silently changing the document.</summary>
    private static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        Encoding encoding = bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) ? new UnicodeEncoding(false, true, true)
            : bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }) ? new UnicodeEncoding(true, true, true)
            : new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), true);
        return (encoding.GetString(bytes.AsSpan(encoding.GetPreamble().Length)), encoding);
    }

    /// <summary>Does not decode escape sequences globally; literal HTML text requires real characters while scripts keep their syntax.</summary>
    private static void ValidateHtml(string path, string text)
    {
        if (!new[] { ".html", ".htm" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) { return; }
        string markup = Regex.Replace(text, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (Regex.IsMatch(markup, @"\\u[0-9a-fA-F]{4}", RegexOptions.None, TimeSpan.FromSeconds(1)))
        { throw new FileEditException("literal_html_escape", "HTML contains a literal Unicode escape outside script/style. Use actual Unicode characters or HTML entities; no automatic decoding was performed."); }
    }

    /// <summary>Rejects redirected ancestry and remote paths; this check supplements, and never grants, native permissions.</summary>
    private static string SafePath(string path)
    {
        path = Path.GetFullPath(path);
        if (Permissions.PermissionPolicy.IsNetworkPath(path)) { throw new IOException("Remote edit paths are unsupported."); }
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
        { if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Linked edit paths are unsupported."); } }
        return path;
    }

    /// <summary>Requires an exact source version before a mutation or recovery.</summary>
    private static void RequireHash(byte[] bytes, string expected, bool effectUncertain = false)
    { if (expected.Length != 64 || !string.Equals(Hash(bytes), expected, StringComparison.OrdinalIgnoreCase)) { throw new FileEditException(effectUncertain ? "verification_changed" : "version_changed", "The file version changed. Read it again before editing or rollback.", effectUncertain); } }
    /// <summary>Computes the complete byte-level source identity.</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    /// <summary>Resolves only native GUID receipt names.</summary>
    private string ReceiptPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".json");
    /// <summary>Resolves the native immutable preimage.</summary>
    private string BackupPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".original");
    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}

using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.Core.Engine;

/// <summary>Atomically stores current-user protected task checkpoints outside shared Markdown and derived indexes.</summary>
public sealed class FileCognitiveTaskJournal(string directory, IPrivateDataProtector protector) : ICognitiveTaskJournal
{
    private readonly string _directory = Path.GetFullPath(directory);
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 16 };
    private const int MaximumBytes = 262144;

    /// <inheritdoc />
    public async Task SaveAsync(CognitiveTaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        checkpoint = checkpoint.Validated(); ValidateDirectory(); Directory.CreateDirectory(_directory);
        if (!File.Exists(PathFor(checkpoint.Id)) && Directory.EnumerateFiles(_directory, "*.task").Take(200).Count() >= 200)
        { throw new InvalidDataException("Private task retention bound reached; remove a reviewed checkpoint before creating more tasks."); }
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(checkpoint, Options);
        if (plaintext.Length > MaximumBytes) { throw new InvalidDataException("Task checkpoint exceeds storage bounds."); }
        byte[] protectedBytes;
        try { protectedBytes = protector.Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (protectedBytes.Length > MaximumBytes) { throw new InvalidDataException("Protected task checkpoint exceeds storage bounds."); }
        string path = PathFor(checkpoint.Id), temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(protectedBytes, cancellationToken).ConfigureAwait(false); await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(true); }
            cancellationToken.ThrowIfCancellationRequested(); ValidateDirectory(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    /// <inheritdoc />
    public async Task<CognitiveTaskCheckpoint?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) { return null; }
        ValidateDirectory(); string path = PathFor(id); if (!File.Exists(path)) { return null; }
        PermissionPolicy.ValidateResources(new(PermissionOperation.FileReadLocal, "Private task journal", "Validate checkpoint", [path]), []);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > MaximumBytes) { throw new InvalidDataException("Protected task checkpoint exceeds storage bounds."); }
        byte[] ciphertext = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(ciphertext, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = protector.Unprotect(ciphertext);
        try
        {
            if (plaintext.Length > MaximumBytes) { throw new InvalidDataException("Task checkpoint exceeds storage bounds."); }
            var checkpoint = (JsonSerializer.Deserialize<CognitiveTaskCheckpoint>(plaintext, Options) ?? throw new InvalidDataException("Task checkpoint is empty.")).Validated();
            if (checkpoint.Id != id) { throw new InvalidDataException("Task checkpoint identity mismatch."); }
            return checkpoint;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CognitiveTaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default)
    {
        ValidateDirectory(); if (!Directory.Exists(_directory)) { return []; }
        var checkpoints = new List<CognitiveTaskCheckpoint>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.task").OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out Guid id)) { continue; }
            // A corrupt entry stays on disk for inspection; it cannot hide valid independent tasks or gain execution rights.
            try { if (await ReadAsync(id, cancellationToken).ConfigureAwait(false) is { } checkpoint) { checkpoints.Add(checkpoint); } }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException or Permissions.PermissionDeniedException) { }
        }
        return checkpoints.OrderByDescending(checkpoint => checkpoint.Updated).ToArray();
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateDirectory();
        if (id == Guid.Empty) { throw new ArgumentException("A task identity is required.", nameof(id)); }
        string path = PathFor(id);
        PermissionPolicy.ValidateResources(new(PermissionOperation.FileWrite, "Private task journal", "Remove selected checkpoint", [path]), []);
        File.Delete(path); return Task.CompletedTask;
    }

    /// <summary>Derives a filename exclusively from a validated native task identity.</summary>
    private string PathFor(Guid id) => Path.Combine(_directory, id.ToString("N") + ".task");
    /// <summary>Rejects network and reparse-point task storage without consulting Brain or model policy.</summary>
    private void ValidateDirectory() => PermissionPolicy.ValidateResources(new(PermissionOperation.FileWrite, "Private task journal", "Validate private storage", [_directory]), []);
}

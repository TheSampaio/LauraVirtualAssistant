using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.Core.Brain;

/// <summary>Stores bounded protected repository inventory checkpoints independently from Markdown knowledge.</summary>
public sealed class FileRepositoryStudyStore(string directory, IPrivateDataProtector protector) : IRepositoryStudyStore
{
    private readonly string _directory = Path.GetFullPath(directory);
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 16 };

    /// <inheritdoc />
    public async Task SaveAsync(RepositoryStudyState state, CancellationToken cancellationToken = default)
    {
        state = state.Validated(); Validate(); Directory.CreateDirectory(_directory);
        string path = PathFor(state.Id);
        if (!File.Exists(path) && Directory.EnumerateFiles(_directory, "*.study").Take(100).Count() >= 100) { throw new InvalidDataException("Repository study retention bound reached."); }
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state, Options); byte[] encrypted;
        try { if (plaintext.Length > MaximumBytes) { throw new InvalidDataException("Study inventory exceeds its storage bound."); } encrypted = protector.Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (encrypted.Length > MaximumBytes) { throw new InvalidDataException("Protected study exceeds its storage bound."); }
        string temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await output.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false); await output.FlushAsync(cancellationToken).ConfigureAwait(false); output.Flush(true); }
            cancellationToken.ThrowIfCancellationRequested(); Validate(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    /// <inheritdoc />
    public async Task<RepositoryStudyState?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) { return null; } Validate(); string path = PathFor(id); if (!File.Exists(path)) { return null; }
        PermissionPolicy.ValidateResources(new(PermissionOperation.FileReadLocal, "Private study journal", "Validate checkpoint", [path]), []);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous);
        if (input.Length is <= 0 or > MaximumBytes) { throw new InvalidDataException("Protected study exceeds its storage bound."); }
        byte[] encrypted = new byte[(int)input.Length]; await input.ReadExactlyAsync(encrypted, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = protector.Unprotect(encrypted);
        try
        {
            if (plaintext.Length > MaximumBytes) { throw new InvalidDataException("Study inventory exceeds its storage bound."); }
            var state = (JsonSerializer.Deserialize<RepositoryStudyState>(plaintext, Options) ?? throw new InvalidDataException("Study inventory is empty.")).Validated();
            if (state.Id != id) { throw new InvalidDataException("Repository study identity mismatch."); } return state;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    /// <summary>Derives a private path from a native identity, never from repository content.</summary>
    private string PathFor(Guid id) => Path.Combine(_directory, id.ToString("N") + ".study");
    /// <summary>Rejects redirected, network and protected operating-system storage.</summary>
    private void Validate() => PermissionPolicy.ValidateResources(new(PermissionOperation.FileWrite, "Private study journal", "Validate private storage", [_directory]), []);
}

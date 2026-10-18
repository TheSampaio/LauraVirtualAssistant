using System.Security.Cryptography;
using Laura.Core.Permissions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Retires Laura-owned credentials and offers explicit hash-verified cleanup of its former model downloads.</summary>
public sealed class LegacyProviderCleanup(string directory)
{
    private static readonly (string Name, long Size, string Hash)[] Artifacts =
    [
        ("qwen3.5-4b-q4_k_m.gguf", 2707513696, "25082a7dd3776cc3c741c6347d3bd04523f05796607b3fbc32fa3a25dfa1418c"),
        ("qwen2.5-0.5b-instruct-q4_k_m.gguf", 491400032, "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db"),
        ("whisper-base.bin", 147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe")
    ];

    /// <summary>Removes only Laura's retired protected OpenAI credential, without decrypting it or touching another application's storage.</summary>
    public void RetireCredential()
    {
        string path = Path.Combine(directory, "openai-key.dat");
        Validate(path);
        File.Delete(path);
    }

    /// <summary>Deletes only exact former downloads after trusted UI confirmation. Returns: Count of verified owned artifacts removed; unknown/shared files are preserved.</summary>
    public async Task<int> CleanModelsAsync(CancellationToken cancellationToken = default)
    {
        int removed = 0;
        foreach (var artifact in Artifacts)
        {
            string path = Path.Combine(directory, "Models", artifact.Name);
            Validate(path);
            if (!File.Exists(path) || new FileInfo(path).Length != artifact.Size) { continue; }
            string hash;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            { hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (!hash.Equals(artifact.Hash, StringComparison.OrdinalIgnoreCase)) { continue; }
            Validate(path);
            File.Delete(path); removed++;
        }
        return removed;
    }

    /// <summary>Rejects redirected ancestry before reading or deleting an application-owned artifact.</summary>
    private static void Validate(string path) => PermissionPolicy.ValidateResources(
        new(PermissionOperation.Process, "Retired provider cleanup", "Validate owned migration artifact", [Path.GetFullPath(path)]), []);
}

using System.Security.Cryptography;
using System.Text;
using Laura.Core.Abstractions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Protects the AI Studio credential with Windows DPAPI for the current user.</summary>
public sealed class ProtectedApiKeyStore(string directory) : IApiKeyStore
{
    private readonly string _path = Path.Combine(directory, "google-key.dat");
    /// <inheritdoc />
    public string Read()
    {
        if (!File.Exists(_path)) { return string.Empty; }
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return string.Empty; }
    }
    /// <inheritdoc />
    public void Save(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key)) { File.Delete(_path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path + ".tmp", protectedBytes);
            File.Move(_path + ".tmp", _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}

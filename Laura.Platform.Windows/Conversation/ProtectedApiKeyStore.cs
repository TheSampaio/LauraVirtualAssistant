using System.Security.Cryptography;
using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Protects the AI Studio credential with Windows DPAPI for the current user.</summary>
public sealed class ProtectedApiKeyStore(string directory) : IApiKeyStore
{
    private readonly string _path = Path.Combine(directory, "google-key.dat");
    private readonly object _gate = new();
    private CredentialState _state;
    private long _inspectedAt;

    /// <inheritdoc />
    public CredentialState Inspect()
    {
        lock (_gate)
        {
            // HUD refreshes must not decrypt the same secret on every animation frame.
            if (_inspectedAt != 0 && Environment.TickCount64 - _inspectedAt < 1000) { return _state; }
            _ = Read();
            return _state;
        }
    }

    /// <inheritdoc />
    public string Read()
    {
        lock (_gate)
        {
            try
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
                try
                {
                    string key = Encoding.UTF8.GetString(plain);
                    _state = string.IsNullOrWhiteSpace(key) ? CredentialState.DecryptionFailed : CredentialState.Stored;
                    return key;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (FileNotFoundException) { _state = CredentialState.NotConfigured; }
            catch (DirectoryNotFoundException) { _state = CredentialState.NotConfigured; }
            catch (CryptographicException) { _state = CredentialState.DecryptionFailed; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _state = CredentialState.Unavailable; }
            finally { _inspectedAt = Environment.TickCount64; }
            return string.Empty;
        }
    }
    /// <inheritdoc />
    public void Save(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
        _inspectedAt = 0;
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
}

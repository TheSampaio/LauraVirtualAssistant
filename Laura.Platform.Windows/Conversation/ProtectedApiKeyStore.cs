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
        string previous = Path.Combine(Path.GetDirectoryName(_path)!, "google-key.previous.dat");
        if (string.IsNullOrWhiteSpace(key)) { File.Delete(_path); File.Delete(previous); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(protectedBytes); stream.Flush(true); }
                // Keep the last protected version so an accidental replacement remains recoverable.
                if (File.Exists(_path)) { File.Replace(temporary, _path, previous); }
                else { File.Move(temporary, _path, false); }
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }
}

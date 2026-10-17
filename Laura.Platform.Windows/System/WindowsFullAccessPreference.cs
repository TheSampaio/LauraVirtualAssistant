using System.Security.Cryptography;
using System.Text;

namespace Laura.Platform.Windows.System;

/// <summary>Persists native owner consent separately from editable INI settings using current-user DPAPI.</summary>
public sealed class WindowsFullAccessPreference(string directory)
{
    private readonly string _path = Path.Combine(directory, "Security", "full-access-consent.dat");
    private static readonly byte[] Consent = Encoding.UTF8.GetBytes("Laura.FullAccess.OwnerConfirmed.v1");
    private readonly object _gate = new();
    private bool? _confirmed;

    /// <summary>Checks the separate native preference; missing or invalid state never grants full access.</summary>
    public bool IsConfirmed()
    {
        lock (_gate)
        {
        if (_confirmed is { } known) { return known; }
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 4096) { _confirmed = false; return false; }
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            try { _confirmed = CryptographicOperations.FixedTimeEquals(plain, Consent); return _confirmed.Value; }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { _confirmed = false; return false; }
        }
    }

    /// <summary>Saves only an explicit native confirmation, or revokes the saved preference. Args: confirmed: Result of the trusted owner's settings confirmation.</summary>
    public void Save(bool confirmed)
    {
        lock (_gate)
        {
        if (!confirmed) { _confirmed = false; File.Delete(_path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] protectedBytes = ProtectedData.Protect(Consent, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path + ".tmp", protectedBytes);
        File.Move(_path + ".tmp", _path, true);
        _confirmed = true;
        }
    }
}

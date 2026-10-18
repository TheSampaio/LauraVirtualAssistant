using System.Security.Cryptography;
using Laura.Core.Abstractions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Protects private task state with Windows current-user DPAPI independently of API credentials.</summary>
public sealed class WindowsPrivateDataProtector : IPrivateDataProtector
{
    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
    /// <inheritdoc />
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
}

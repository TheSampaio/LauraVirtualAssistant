using System.Security.Cryptography;
using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Uses a test-owned key to exercise private persistence without platform DPAPI or credentials.</summary>
internal sealed class TestPrivateDataProtector : IPrivateDataProtector
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        byte[] output = new byte[28 + plaintext.Length]; RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(_key, 16); aes.Encrypt(output.AsSpan(0, 12), plaintext, output.AsSpan(28), output.AsSpan(12, 16)); return output;
    }
    /// <inheritdoc />
    public byte[] Unprotect(byte[] ciphertext)
    {
        byte[] output = new byte[ciphertext.Length - 28]; using var aes = new AesGcm(_key, 16);
        aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), output); return output;
    }
}

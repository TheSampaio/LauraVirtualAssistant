namespace Laura.Core.Abstractions;

/// <summary>Protects private assistant checkpoints at the platform boundary.</summary>
public interface IPrivateDataProtector
{
    /// <summary>Protects a bounded plaintext payload for the current user.</summary>
    byte[] Protect(byte[] plaintext);
    /// <summary>Authenticates and restores a protected payload; failures must preserve the original file.</summary>
    byte[] Unprotect(byte[] ciphertext);
}

namespace Laura.Core.Configuration;

/// <summary>Describes credential health without exposing the credential or conflating it with provider selection.</summary>
public enum CredentialState
{
    /// <summary>No protected credential exists.</summary>
    NotConfigured,
    /// <summary>The protected credential is readable; remote acceptance has not been established.</summary>
    Stored,
    /// <summary>Storage cannot currently be accessed.</summary>
    Unavailable,
    /// <summary>Protected bytes cannot be decrypted for this Windows identity.</summary>
    DecryptionFailed,
    /// <summary>A remote credential validation is in progress.</summary>
    ValidationPending,
    /// <summary>The provider accepted the credential for the current session.</summary>
    Validated,
    /// <summary>The provider rejected authentication without deleting local storage.</summary>
    AuthenticationFailed,
    /// <summary>The network failed without establishing that the credential is invalid.</summary>
    NetworkUnavailable,
}

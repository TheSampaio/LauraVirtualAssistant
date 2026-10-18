namespace Laura.Core.Abstractions;

/// <summary>Stores the Google credential separately from ordinary settings.</summary>
public interface IApiKeyStore
{
    /// <summary>Inspects protected storage without returning a credential. Returns: Storage health, independently of authentication.</summary>
    Configuration.CredentialState Inspect() => string.IsNullOrEmpty(Read()) ? Configuration.CredentialState.NotConfigured : Configuration.CredentialState.Stored;

    /// <summary>Gets the credential without exposing it to logs or UI labels.</summary>
    string Read();
    /// <summary>Protects and saves a credential; an empty value removes it.</summary>
    void Save(string key);
}

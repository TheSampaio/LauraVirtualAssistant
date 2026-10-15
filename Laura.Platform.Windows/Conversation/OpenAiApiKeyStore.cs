using Laura.Core.Abstractions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Keeps the optional OpenAI credential independently protected for the current Windows user.</summary>
public sealed class OpenAiApiKeyStore(string directory) : IApiKeyStore
{
    private readonly ProtectedApiKeyStore _store = new(directory, openAi: true);
    /// <inheritdoc />
    public string Read() => _store.Read();
    /// <inheritdoc />
    public void Save(string key) => _store.Save(key);
}

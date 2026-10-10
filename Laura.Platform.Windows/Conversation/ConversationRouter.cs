using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Routes to the explicitly selected backend. No cloud or automatic fallback is enabled.</summary>
public sealed class ConversationRouter : IConversationEngine
{
    private readonly ISettingsService _settings;
    private readonly EmbeddedConversationEngine _embedded;
    private readonly OllamaConversationEngine _ollama;

    /// <summary>Connects existing and embedded adapters without creating another orchestration loop.</summary>
    public ConversationRouter(ISettingsService settings, EmbeddedConversationEngine embedded, OllamaConversationEngine ollama)
    {
        _settings = settings;
        _embedded = embedded;
        _ollama = ollama;
    }

    private IConversationEngine Selected => _settings.Current.GenerativeAi.Provider == "ollama" ? _ollama : _embedded;
    /// <inheritdoc />
    public bool IsAvailable => Selected.IsAvailable;
    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) =>
        Selected.AskAsync(turn, cancellationToken);
}

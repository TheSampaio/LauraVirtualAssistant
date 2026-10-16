using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>Generates private, bounded local agent responses without publishing tool arguments to the chat surface.</summary>
public interface ILocalToolInferenceEngine : ILocalInferenceEngine
{
    /// <summary>Generates one private round. Args: turn: Original user turn and evidence; instructions: Native tool protocol and schemas; exchange: Complete current-turn calls and observations; cancellationToken: Round lifetime. Returns: Untrusted model output requiring strict parsing before dispatch.</summary>
    Task<string?> AskWithToolsAsync(ConversationTurn turn, LocalToolPrompt instructions, IReadOnlyList<ConversationMessage> exchange, CancellationToken cancellationToken);
}

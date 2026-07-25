using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>
/// Inert generative engine, registered while no real provider is connected.
///
/// Applying the Null Object pattern here avoids null checks scattered through the
/// dispatcher and keeps the no-AI path as the normal path, not an exception.
/// </summary>
public sealed class NullConversationEngine : IConversationEngine
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

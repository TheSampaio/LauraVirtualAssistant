using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Retains native working-turn identity and messages for queue/recovery tests without a personal vault.</summary>
internal sealed class WorkingConversationMemory : IConversationMemory
{
    private readonly List<ConversationMessage> _messages = [];
    /// <inheritdoc />
    public ConversationWorkingContext WorkingContext { get; private set; } = new(Guid.Empty, "");
    /// <inheritdoc />
    public void SetWorkingContext(ConversationWorkingContext context) => WorkingContext = context;
    /// <inheritdoc />
    public void Initialize() { }
    /// <inheritdoc />
    public void Append(ConversationMessage message) { _messages.Add(message); if (message.Source != ConversationMessageSource.Assistant) { WorkingContext = new(message.TurnId, message.Text); } }
    /// <inheritdoc />
    public IReadOnlyList<ConversationMessage> ReadRecent(int limit) => _messages.TakeLast(limit).ToArray();
    /// <inheritdoc />
    public IReadOnlyList<MemoryFact> Search(string query) => [];
    /// <inheritdoc />
    public void SetFact(string key, string text) => throw new NotSupportedException();
    /// <inheritdoc />
    public void DeleteFact(string key) => throw new NotSupportedException();
    /// <inheritdoc />
    public void ClearHistory() { _messages.Clear(); WorkingContext = new(Guid.Empty, ""); }
}

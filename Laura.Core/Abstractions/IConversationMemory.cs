using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Abstractions;

/// <summary>Unified local storage for history and explicit user memories.</summary>
public interface IConversationMemory
{
    /// <summary>Creates or migrates supported schema versions; unsupported/corrupt data is preserved.</summary>
    void Initialize();
    /// <summary>Appends one message, bounding retained conversation history.</summary>
    void Append(ConversationMessage message);
    /// <summary>Returns recent messages in chronological order.</summary>
    IReadOnlyList<ConversationMessage> ReadRecent(int limit);
    /// <summary>Searches explicit user memories using literal text.</summary>
    IReadOnlyList<MemoryFact> Search(string query);
    /// <summary>Stores an explicit user preference with a stable key.</summary>
    void SetFact(string key, string text);
    /// <summary>Deletes the selected preference.</summary>
    void DeleteFact(string key);
    /// <summary>Deletes conversation messages without deleting explicit memories.</summary>
    void ClearHistory();
}

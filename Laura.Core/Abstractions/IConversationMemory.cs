using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Abstractions;

/// <summary>Unified local storage for history and explicit user memories.</summary>
public interface IConversationMemory
{
    /// <summary>Gets bounded transient user input for grounding proposals; it is never an authorization.</summary>
    string WorkingUserText => "";
    /// <summary>Atomically snapshots the current user turn. Adapters without turn tracking cannot attribute skill usage.</summary>
    ConversationWorkingContext WorkingContext => new(Guid.Empty, WorkingUserText);
    /// <summary>Gets the current transient identity without requiring a text snapshot on real-time diagnostic paths.</summary>
    Guid WorkingTurnId => WorkingContext.TurnId;
    /// <summary>Updates current user input before tool dispatch without creating a persistent fact.</summary>
    void SetWorkingContext(string text) { }
    /// <summary>Updates a partial transcription within the same user turn, without changing its identity or persisting it.</summary>
    void UpdateWorkingContext(string text) => SetWorkingContext(text);
    /// <summary>Creates or migrates supported schema versions; unsupported/corrupt data is preserved.</summary>
    void Initialize();
    /// <summary>Appends one message, bounding retained conversation history.</summary>
    void Append(ConversationMessage message);
    /// <summary>Retains a completed historical turn without replacing a newer user's active working context.</summary>
    void AppendHistory(ConversationMessage message) => Append(message);
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

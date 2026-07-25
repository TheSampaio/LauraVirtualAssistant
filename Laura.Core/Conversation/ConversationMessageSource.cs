namespace Laura.Core.Conversation;

/// <summary>
/// Source of a conversation message shown by the UI.
/// </summary>
public enum ConversationMessageSource
{
    /// <summary>A voice transcript from the user.</summary>
    UserVoice,

    /// <summary>A typed command from the user.</summary>
    UserTyped,

    /// <summary>An assistant response.</summary>
    Assistant,
}

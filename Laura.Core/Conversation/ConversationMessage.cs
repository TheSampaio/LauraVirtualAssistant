namespace Laura.Core.Conversation;

/// <summary>
/// A message observed by the assistant conversation surface.
/// </summary>
/// <param name="Source">Where the message came from.</param>
/// <param name="Text">Message text.</param>
/// <param name="Timestamp">Local time when the message was observed.</param>
public sealed record ConversationMessage(
    ConversationMessageSource Source,
    string Text,
    DateTimeOffset Timestamp);

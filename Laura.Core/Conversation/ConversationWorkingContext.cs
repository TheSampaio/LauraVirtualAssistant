namespace Laura.Core.Conversation;

/// <summary>Transient user-turn identity and bounded grounding text. Neither value is an execution authorization.</summary>
public sealed record ConversationWorkingContext(Guid TurnId, string UserText);

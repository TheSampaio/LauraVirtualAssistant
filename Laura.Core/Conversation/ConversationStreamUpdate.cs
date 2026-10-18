namespace Laura.Core.Conversation;

/// <summary>A provisional visible transcript; persistence occurs only when the real turn completes.</summary>
public sealed record ConversationStreamUpdate(Guid TurnId, string Text, bool Completed);

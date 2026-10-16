namespace Laura.Core.Conversation;

/// <summary>Transports only safe failure metadata across native or network adapters.</summary>
public sealed class ConversationProviderException(ConversationFailure failure) : Exception("The conversation provider failed.")
{
    /// <summary>Gets the closed failure category and conservative effect marker.</summary>
    public ConversationFailure Failure { get; } = failure;
}

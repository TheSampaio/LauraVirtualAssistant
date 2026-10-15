namespace Laura.Core.Conversation;

/// <summary>Following is suspended only by manual navigation; growing content never counts as a manual scroll.</summary>
public sealed class ConversationScrollState
{
    /// <summary>Gets whether layout changes should keep the newest message visible.</summary>
    public bool Following { get; private set; } = true;
    /// <summary>Gets the number of new messages while reading older content.</summary>
    public int Unread { get; private set; }
    /// <summary>Observes a native user-driven position, using a small pixel rounding tolerance.</summary>
    public void ManualPosition(double distanceFromBottom) { Following = distanceFromBottom <= 4; if (Following) { Unread = 0; } }
    /// <summary>Receives a new message without changing the user's scroll intent.</summary>
    public void MessageArrived() { if (!Following) { Unread++; } }
    /// <summary>Returns to the newest message and resumes following.</summary>
    public void Follow() { Following = true; Unread = 0; }
}

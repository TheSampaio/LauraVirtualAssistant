namespace Laura.Core.Conversation;

/// <summary>Closed diagnostic categories that never retain provider messages, credentials or prompts.</summary>
public enum ConversationFailureKind
{
    /// <summary>An unclassified failure occurred.</summary>
    Unknown,
    /// <summary>The credential was rejected.</summary>
    Authentication,
    /// <summary>The account lacks access to the requested resource.</summary>
    AccessDenied,
    /// <summary>The service reported a billing or account usage limit.</summary>
    UsageLimit,
    /// <summary>The service reported throttling or an undifferentiated quota limit.</summary>
    CapacityLimit,
    /// <summary>The configured model could not be found or accessed.</summary>
    ModelUnavailable,
    /// <summary>The input exceeded the model context budget.</summary>
    ContextExceeded,
    /// <summary>The provider deadline elapsed.</summary>
    Timeout,
    /// <summary>The network connection failed.</summary>
    Network,
    /// <summary>The provider returned malformed or incomplete output.</summary>
    InvalidResponse,
    /// <summary>The provider is unavailable or failed internally.</summary>
    Unavailable,
    /// <summary>The local weight bytes failed integrity verification.</summary>
    LocalIntegrity,
    /// <summary>Available memory failed local admission checks.</summary>
    InsufficientMemory,
    /// <summary>The selected backend cannot process the supplied modality.</summary>
    UnsupportedInput,
    /// <summary>The service rejected the request without a more specific safe code.</summary>
    RequestRejected,
    /// <summary>The native agent exhausted its bounded operation budget.</summary>
    OperationLimit,
}

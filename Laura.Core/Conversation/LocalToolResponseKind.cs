namespace Laura.Core.Conversation;

/// <summary>Distinguishes a final answer from one validated structured call or an invalid protocol response.</summary>
public enum LocalToolResponseKind
{
    /// <summary>The response contains no native call envelope.</summary>
    Answer,
    /// <summary>The entire response is exactly one schema-valid registered call.</summary>
    Call,
    /// <summary>The response is malformed, ambiguous, excessive or unsupported.</summary>
    Invalid,
}

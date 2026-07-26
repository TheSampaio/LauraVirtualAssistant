namespace Laura.Core.Engine;

/// <summary>
/// Observable state of the assistant, reflected in the interface indicator.
/// </summary>
public enum AssistantState
{
    /// <summary>The engine has not been started or has been stopped.</summary>
    Stopped,

    /// <summary>Ready to receive typed chat commands or internal speech requests.</summary>
    Ready,

    /// <summary>Running a skill.</summary>
    Working,

    /// <summary>Speaking.</summary>
    Speaking,
}

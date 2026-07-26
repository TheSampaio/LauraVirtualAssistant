namespace Laura.Core.Skills;

/// <summary>
/// Origin of a command received by the assistant.
/// </summary>
public enum SkillRequestSource
{
    /// <summary>Command typed in the interface.</summary>
    Text,

    /// <summary>Command triggered internally by the assistant itself.</summary>
    Internal,
}

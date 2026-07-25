namespace Laura.Core.Engine;

/// <summary>
/// Observable state of the assistant, reflected in the interface indicator.
/// </summary>
public enum AssistantState
{
    /// <summary>The engine has not been started or has been stopped.</summary>
    Stopped,

    /// <summary>Listening for the wake word only.</summary>
    AwaitingWakeWord,

    /// <summary>Woken, waiting for the command.</summary>
    ListeningForCommand,

    /// <summary>Running a skill.</summary>
    Working,

    /// <summary>Speaking.</summary>
    Speaking,

    /// <summary>Listening turned off in settings; the assistant responds through the UI only.</summary>
    ListeningDisabled,

    /// <summary>No usable recognizer or microphone on this machine.</summary>
    RecognitionUnavailable,
}

namespace Laura.Core.Skills;

/// <summary>Explicitly supported desktop capabilities; unknown values are rejected.</summary>
public enum DesktopAction
{
    /// <summary>Read user-approved clipboard text.</summary>
    ReadClipboard,
    /// <summary>Capture the primary screen to local storage after approval.</summary>
    CaptureScreen,
    /// <summary>List the assistant's dedicated file workspace.</summary>
    ListFiles,
    /// <summary>Save a new note in the dedicated workspace.</summary>
    SaveNote,
    /// <summary>Undo the most recently created workspace artifact if unchanged.</summary>
    Undo,
}

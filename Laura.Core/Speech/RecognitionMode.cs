namespace Laura.Core.Speech;

/// <summary>
/// Speech recognizer listening mode.
///
/// The separation exists for precision: a grammar restricted to the trigger almost
/// eliminates accidental activations, while free dictation - required for arbitrary
/// commands - is too noisy to stay active all the time.
/// </summary>
public enum RecognitionMode
{
    /// <summary>Listening suspended; no audio is processed.</summary>
    Idle,

    /// <summary>Listens only for wake phrases ("Ok, Laura").</summary>
    WakeWord,

    /// <summary>Listens for a full command after activation.</summary>
    Command,
}

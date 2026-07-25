namespace Laura.Core.Speech;

/// <summary>
/// Transcription produced by the speech recognizer.
/// </summary>
/// <param name="Text">Texto reconhecido, exatamente como transcrito pelo motor.</param>
/// <param name="Confidence">Engine confidence in the transcription, between 0.0 and 1.0.</param>
/// <param name="Mode">Listening mode active when the transcription was produced.</param>
public sealed record RecognitionResult(string Text, double Confidence, RecognitionMode Mode);

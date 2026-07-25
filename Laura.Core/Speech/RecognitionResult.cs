namespace Laura.Core.Speech;

/// <summary>
/// Transcrição produzida pelo reconhecedor de fala.
/// </summary>
/// <param name="Text">Texto reconhecido, exatamente como transcrito pelo motor.</param>
/// <param name="Confidence">Confiança do motor na transcrição, entre 0.0 e 1.0.</param>
/// <param name="Mode">Modo de escuta ativo quando a transcrição foi produzida.</param>
public sealed record RecognitionResult(string Text, double Confidence, RecognitionMode Mode);

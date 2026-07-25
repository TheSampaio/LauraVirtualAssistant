using Laura.Core.Configuration;

namespace Laura.Core.Speech;

/// <summary>
/// Pedido de fala entregue ao sintetizador.
/// </summary>
/// <param name="Text">Text to speak, in natural language and without markup.</param>
/// <param name="Voice">Perfil de voz — timbre, ritmo, tom e volume — a aplicar.</param>
/// <param name="Culture">Text culture, in BCP-47 format, used for pronunciation.</param>
public sealed record SpeechRequest(string Text, VoiceProfile Voice, string Culture);

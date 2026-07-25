using Laura.Core.Configuration;

namespace Laura.Core.Speech;

/// <summary>
/// Pedido de fala entregue ao sintetizador.
/// </summary>
/// <param name="Text">Texto a ser falado, em linguagem natural e sem marcação.</param>
/// <param name="Voice">Perfil de voz — timbre, ritmo, tom e volume — a aplicar.</param>
/// <param name="Culture">Cultura do texto, no formato BCP-47, usada na pronúncia.</param>
public sealed record SpeechRequest(string Text, VoiceProfile Voice, string Culture);

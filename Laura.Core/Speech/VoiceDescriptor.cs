namespace Laura.Core.Speech;

/// <summary>
/// Descreve uma voz de síntese disponível no sistema.
/// </summary>
/// <param name="Name">Identificador da voz junto ao motor de síntese.</param>
/// <param name="DisplayName">Nome legível para exibição na interface.</param>
/// <param name="Culture">Cultura falada pela voz, no formato BCP-47.</param>
/// <param name="IsFemale">
/// <see langword="true"/> quando o motor declara a voz como feminina. Laura prefere
/// vozes femininas ao escolher um padrão automaticamente.
/// </param>
public sealed record VoiceDescriptor(string Name, string DisplayName, string Culture, bool IsFemale);

namespace Laura.Core.Speech;

/// <summary>
/// Describes a synthesis voice available on the system.
/// </summary>
/// <param name="Name">Voice identifier in the synthesis engine.</param>
/// <param name="DisplayName">Readable name for display in the interface.</param>
/// <param name="Culture">Cultura falada pela voz, no formato BCP-47.</param>
/// <param name="IsFemale">
/// <see langword="true"/> quando o motor declara a voz como feminina. Laura prefere
/// female voices when automatically choosing a default.
/// </param>
public sealed record VoiceDescriptor(string Name, string DisplayName, string Culture, bool IsFemale);

namespace Laura.Core.Speech;

/// <summary>
/// Describes a synthesis voice available on the system.
/// </summary>
/// <param name="Name">Voice identifier in the synthesis engine.</param>
/// <param name="DisplayName">Readable name for display in the interface.</param>
/// <param name="Culture">Culture spoken by the voice, in BCP-47 format.</param>
/// <param name="IsFemale">
/// <see langword="true"/> when the engine reports the voice as feminine. Laura prefers
/// female voices when automatically choosing a default.
/// </param>
public sealed record VoiceDescriptor(string Name, string DisplayName, string Culture, bool IsFemale);

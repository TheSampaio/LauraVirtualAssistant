using Laura.Core.Configuration;

namespace Laura.Core.Speech;

/// <summary>
/// Speech request passed to the synthesizer.
/// </summary>
/// <param name="Text">Text to speak, in natural language and without markup.</param>
/// <param name="Voice">Voice profile - timbre, rate, pitch, and volume - to apply.</param>
/// <param name="Culture">Text culture, in BCP-47 format, used for pronunciation.</param>
public sealed record SpeechRequest(string Text, VoiceProfile Voice, string Culture);

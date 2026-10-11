namespace Laura.Core.Configuration;

/// <summary>Gemini Live model and voice identifiers used by the reference assistant.</summary>
public static class GeminiLiveOptions
{
    /// <summary>Gets the five selectable native audio voices.</summary>
    public static IReadOnlyList<string> Voices { get; } = Array.AsReadOnly(new[] { "Charon", "Puck", "Kore", "Fenrir", "Aoede" });
    /// <summary>Gets the primary Live model.</summary>
    public const string Model = "gemini-3.1-flash-live-preview";
    /// <summary>Gets the native audio fallback used when the primary model is unavailable.</summary>
    public const string FallbackModel = "gemini-2.5-flash-native-audio-preview-12-2025";
}

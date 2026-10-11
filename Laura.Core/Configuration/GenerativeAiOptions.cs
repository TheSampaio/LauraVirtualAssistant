namespace Laura.Core.Configuration;

/// <summary>
/// Configuration for Google AI Studio conversation, native voice and explicit desktop tools.
/// </summary>
public sealed record GenerativeAiOptions
{
    /// <summary>
    /// Gets the default Gemini options; a separately protected key is still required.
    /// </summary>
    public static GenerativeAiOptions Default { get; } = new();

    /// <summary>
    /// Gets whether cloud conversation and native voice are enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the sole supported provider identifier, <c>gemini</c>.
    /// </summary>
    public string Provider { get; init; } = "gemini";

    /// <summary>
    /// Gets a legacy configuration field retained for INI compatibility; Live uses its fixed Google endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Gets an optional Gemini model override; empty selects the reviewed Live default.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the native Gemini Live voice, shared with Mark-LV.</summary>
    public string LiveVoice { get; init; } = "Aoede";

    /// <summary>Gets whether an explicitly started Live microphone stays open between turns.</summary>
    public bool ContinuousListening { get; init; } = true;

    /// <summary>Gets the silence interval used by server-side speech turn detection.</summary>
    public int SilenceDurationMs { get; init; } = 650;

    /// <summary>
    /// Gets the system instruction that defines Laura's persona for the model.
    /// </summary>
    public string Persona { get; init; } =
        "You are Laura, a discreet personal assistant: calm, capable, precise, " +
        "and quietly confident. Speak as if replying aloud, never as a chatbot. Keep answers " +
        "to one short sentence by default, two only when truly needed. Do not use lists, " +
        "markdown, emojis, long explanations, disclaimers, or filler. If the user asks for " +
        "an action you cannot perform, say so briefly and offer the closest useful next step. " +
        "Sound composed and practical, with subtle warmth, never overly enthusiastic.";

    /// <summary>
    /// Gets the maximum time to wait for a model response.
    ///
    /// A short limit is deliberate: a voice assistant that is slow to answer is
    /// worse than one that admits it did not understand.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Returns a copy with safe, trimmed values.
    ///
    /// Returns:
    ///     Sanitized generative AI options.
    /// </summary>
    public GenerativeAiOptions Sanitized() => this with
    {
        Provider = "gemini",
        ContinuousListening = true,
        SilenceDurationMs = Math.Clamp(SilenceDurationMs, 300, 2000),
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? Default.Endpoint : Endpoint.Trim(),
        Model = Model?.Trim() is { Length: <= 100 } model && !model.Contains("..", StringComparison.Ordinal)
            && model.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            && (model.Length == 0 || model.StartsWith("gemini-", StringComparison.Ordinal))
                ? model : string.Empty,
        Persona = string.IsNullOrWhiteSpace(Persona) ? Default.Persona : Persona.Trim(),
        LiveVoice = GeminiLiveOptions.Voices.Contains(LiveVoice, StringComparer.Ordinal) ? LiveVoice : Default.LiveVoice,
        Timeout = Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10) ? Default.Timeout : Timeout,
    };
}

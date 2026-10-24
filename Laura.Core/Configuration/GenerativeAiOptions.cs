namespace Laura.Core.Configuration;

/// <summary>
/// Shared conversational preferences and optional Google AI Studio access.
/// </summary>
public sealed record GenerativeAiOptions
{
    private const string LegacyDefaultPersona =
        "You are Laura, a discreet personal assistant: calm, capable, precise, " +
        "and quietly confident. Speak as if replying aloud, never as a chatbot. Keep answers " +
        "to one short sentence by default, two only when truly needed. Do not use lists, " +
        "markdown, emojis, long explanations, disclaimers, or filler. If the user asks for " +
        "an action you cannot perform, say so briefly and offer the closest useful next step. " +
        "Sound composed and practical, with subtle warmth, never overly enthusiastic.";
    /// <summary>
    /// Gets the default conversational preferences.
    /// </summary>
    public static GenerativeAiOptions Default { get; } = new();

    /// <summary>
    /// Gets explicit consent to use Gemini Live.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the protected cloud provider identity.
    /// </summary>
    public string Provider { get; init; } = "gemini";

    /// <summary>
    /// Gets the optional Gemini Live model override.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the Gemini native audio voice.</summary>
    public string LiveVoice { get; init; } = "Aoede";
    /// <summary>Chooses the Light inference provider without granting either provider access by itself.</summary>
    public LightProviderSelection LightProvider { get; init; } = LightProviderSelection.Gemini;

    /// <summary>Gets whether explicitly started local microphone input resumes after each completed reply.</summary>
    public bool ContinuousListening { get; init; } = true;

    /// <summary>Gets the silence interval used by local speech endpoint detection.</summary>
    public int SilenceDurationMs { get; init; } = 650;

    /// <summary>
    /// Gets the system instruction that defines Laura's persona for the model.
    /// </summary>
    public string Persona { get; init; } =
        "Be a thoughtful, capable person in conversation: calm, perceptive, discreet and quietly confident. " +
        "Be concise for simple questions and thorough when the task needs it. Use natural language, " +
        "subtle warmth and occasional well-timed humor without forced enthusiasm. " +
        "Avoid stock phrases, excessive apologies and unverified claims. Ask only when an important detail is missing. " +
        "Keep Portuguese idiomatic to Brazil and English idiomatic to the United States. " +
        "Use feminine self-reference in Portuguese when appropriate. Preserve literal technical content.";

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
        Enabled = Enabled && string.Equals(Provider?.Trim(), "gemini", StringComparison.OrdinalIgnoreCase),
        Provider = "gemini",
        ContinuousListening = true,
        SilenceDurationMs = Math.Clamp(SilenceDurationMs, 300, 2000),
        Model = Model?.Trim() is { Length: <= 100 } model && !model.Contains("..", StringComparison.Ordinal)
            && model.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            && (model.Length == 0 || model.StartsWith("gemini-", StringComparison.Ordinal)) ? model : string.Empty,
        Persona = string.IsNullOrWhiteSpace(Persona) || Persona.Trim() == LegacyDefaultPersona ? Default.Persona : Persona.Trim(),
        LiveVoice = GeminiLiveOptions.Voices.Contains(LiveVoice, StringComparer.Ordinal) ? LiveVoice : "Aoede",
        LightProvider = Enum.IsDefined(LightProvider) ? LightProvider : LightProviderSelection.Gemini,
        Timeout = Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10) ? Default.Timeout : Timeout,
    };
}

/// <summary>Explicit owner preference for low-cost Light inference.</summary>
public enum LightProviderSelection
{
    /// <summary>Use the optional local GGUF model and local voice.</summary>
    Local,
    /// <summary>Use authorized Gemini Live inference and native audio.</summary>
    Gemini
}

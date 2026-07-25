namespace Laura.Core.Configuration;

/// <summary>
/// Configuration for the generative mode — the optional add-on that lets Laura
/// answer outside the fixed set of skills.
///
/// When <see cref="Enabled"/> is <see langword="false"/> the assistant runs fully
/// offline; no network request is made.
/// </summary>
public sealed record GenerativeAiOptions
{
    /// <summary>
    /// Gets the default options, with the generative mode turned off.
    /// </summary>
    public static GenerativeAiOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether unrecognized commands should be forwarded to
    /// the generative model.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the identifier of the provider to use, for example <c>ollama</c>.
    /// </summary>
    public string Provider { get; init; } = "ollama";

    /// <summary>
    /// Gets the base address of the service, typically a local instance.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Gets the name of the model to load in the provider.
    ///
    /// Empty by default on purpose: the UI only offers models the provider has
    /// actually pulled, so nothing is hard-coded here.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the system instruction that defines Laura's persona for the model.
    /// </summary>
    public string Persona { get; init; } =
        "You are Laura, a discreet Jarvis-like personal assistant: calm, capable, precise, " +
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
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Returns a copy with safe, trimmed values.
    ///
    /// Returns:
    ///     Sanitized generative AI options.
    /// </summary>
    public GenerativeAiOptions Sanitized() => this with
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? Default.Provider : Provider.Trim(),
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? Default.Endpoint : Endpoint.Trim(),
        Model = Model.Trim(),
        Persona = string.IsNullOrWhiteSpace(Persona) ? Default.Persona : Persona.Trim(),
        Timeout = Timeout <= TimeSpan.Zero ? Default.Timeout : Timeout,
    };
}

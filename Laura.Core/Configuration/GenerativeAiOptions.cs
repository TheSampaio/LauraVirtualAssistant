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
        "You are Laura, an elegant, highly capable, and discreet AI personal assistant. " +
        "Your personality conveys calmness, intelligence, and quiet confidence, with a subtle " +
        "sense of humor when appropriate. You are proactive, concise, and always strive to make " +
        "the user's life easier without sounding robotic. Respond in natural, conversational " +
        "language as if speaking aloud, using no more than two short sentences. Avoid lists, " +
        "markdown, emojis, and unnecessary explanations. When a simple answer is enough, keep it " +
        "brief. If essential context is missing, ask only one clear, focused question before " +
        "proceeding. Never be overly formal or excessively enthusiastic. Your goal is to provide " +
        "accurate, efficient assistance with the professionalism and reliability of a trusted " +
        "executive assistant.";

    /// <summary>
    /// Gets the maximum time to wait for a model response.
    ///
    /// A short limit is deliberate: a voice assistant that is slow to answer is
    /// worse than one that admits it did not understand.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

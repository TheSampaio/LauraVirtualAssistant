using Laura.Core.Configuration;

namespace Laura.Core.Models;

/// <summary>Separates account model visibility from reviewed API compatibility. Unknown identifiers remain unverified.</summary>
public static class ProviderModelCompatibility
{
    private static readonly HashSet<string> ResponsesModels = new(StringComparer.Ordinal)
    {
        "gpt-6-astra", "gpt-6.1-sol", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5",
        "gpt-5.4", "gpt-5.4-mini", "gpt-5.4-nano", "gpt-5.2", "gpt-5.1", "gpt-5", "gpt-5-mini", "gpt-5-nano",
        "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano", "o3", "o4-mini", "o1",
    };
    // models.list omits lifecycle dates; these specific retirements were reviewed against the official deprecation schedule.
    private static readonly HashSet<string> DeprecatedModels = new(StringComparer.Ordinal)
    { "gpt-5", "gpt-5-mini", "gpt-5-nano", "gpt-5-2025-08-07", "gpt-5-mini-2025-08-07", "gpt-5-nano-2025-08-07",
        "o3-2025-04-16", "gpt-4.1-nano", "gpt-4.1-nano-2025-04-14", "o1", "o1-2024-12-17", "o4-mini", "o4-mini-2025-04-16" };

    /// <summary>Identifies explicitly reviewed OpenAI retirements without inventing a replacement or changing selection.</summary>
    public static bool IsDeprecated(string id) => DeprecatedModels.Contains(id);

    /// <summary>Checks identifier syntax without pretending the prefix establishes a model's capabilities.</summary>
    public static bool ValidIdentifier(string? id) => id is { Length: > 0 and <= 200 } && !id.Contains("..", StringComparison.Ordinal)
        && id.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    /// <summary>Gets capabilities for reviewed Responses families/snapshots, including supported non-GPT identifiers.</summary>
    public static ModelCapabilities OpenAi(string id)
    {
        string family = id;
        if (id.Length > 11 && id[^11] == '-' && DateOnly.TryParseExact(id[^10..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        { family = id[..^11]; }
        return ResponsesModels.Contains(family) ? ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.Tools | ModelCapabilities.Images : ModelCapabilities.None;
    }

    /// <summary>Classifies Gemini generation methods while restricting native Live to independently reviewed identifiers.</summary>
    public static ModelCapabilities Gemini(string id, IReadOnlySet<string> methods)
    {
        ModelCapabilities capabilities = methods.Contains("generateContent") ? ModelCapabilities.Text | ModelCapabilities.Streaming : ModelCapabilities.None;
        if (methods.Contains("embedContent") || methods.Contains("embedText")) { capabilities |= ModelCapabilities.Embedding; }
        if (id == GeminiLiveOptions.Model || id == GeminiLiveOptions.FallbackModel)
        { capabilities |= ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.Tools | ModelCapabilities.LiveAudio | ModelCapabilities.Images; }
        return capabilities;
    }
}

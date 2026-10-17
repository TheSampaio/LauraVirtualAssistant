using Laura.Core.Configuration;

namespace Laura.Core.Models;

/// <summary>Separates account model visibility from reviewed API compatibility. Unknown identifiers remain unverified.</summary>
public static class ProviderModelCompatibility
{
    /// <summary>Checks identifier syntax without pretending the prefix establishes a model's capabilities.</summary>
    public static bool ValidIdentifier(string? id) => id is { Length: > 0 and <= 200 } && !id.Contains("..", StringComparison.Ordinal)
        && id.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

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

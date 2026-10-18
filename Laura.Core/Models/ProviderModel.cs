namespace Laura.Core.Models;

/// <summary>Reviewed protocol capabilities; discovery alone never grants tool or disclosure authorization.</summary>
[Flags]
public enum ModelCapabilities
{
    /// <summary>No reviewed capability.</summary>
    None = 0,
    /// <summary>Text generation.</summary>
    Text = 1,
    /// <summary>Streaming text.</summary>
    Streaming = 2,
    /// <summary>Structured function calls.</summary>
    Tools = 4,
    /// <summary>Image input.</summary>
    Images = 8,
    /// <summary>Native Live audio.</summary>
    LiveAudio = 16,
    /// <summary>Embedding vectors.</summary>
    Embedding = 32,
}

/// <summary>A real provider-returned model with independently classified compatibility.</summary>
public sealed record ProviderModel(string Id, string Name, ModelCapabilities Capabilities, int? ContextTokens = null, bool Deprecated = false);

/// <summary>Last valid catalogue and safe refresh outcome; missing metadata is not invented.</summary>
public sealed record ProviderModelCatalogue(string Provider, IReadOnlyList<ProviderModel> Models, DateTimeOffset? FetchedAt, bool Stale, string? Error);

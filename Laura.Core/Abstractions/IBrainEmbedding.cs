namespace Laura.Core.Abstractions;

/// <summary>Local embedding boundary with an explicit model identity for cache invalidation.</summary>
public interface IBrainEmbedding
{
    /// <summary>Gets the fingerprint of the installed model and tokenizer, or an empty value when unavailable.</summary>
    string ModelId { get; }
    /// <summary>Produces a normalized vector without transmitting document content.</summary>
    float[] Embed(string text);
}

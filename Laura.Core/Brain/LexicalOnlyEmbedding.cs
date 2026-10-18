using Laura.Core.Abstractions;

namespace Laura.Core.Brain;

/// <summary>Disables neural inference while preserving the rebuildable FTS5 and graph retrieval pipeline.</summary>
public sealed class LexicalOnlyEmbedding : IBrainEmbedding
{
    /// <inheritdoc />
    public string ModelId => string.Empty;
    /// <inheritdoc />
    public float[] Embed(string text) => [];
}

namespace Laura.Core.Brain;

/// <summary>Local settings kept outside the portable Markdown vault.</summary>
public sealed record BrainOptions
{
    /// <summary>Gets the selected vault, or an empty value for the user's application-data default.</summary>
    public string Directory { get; init; } = "";
    /// <summary>Gets whether durable user declarations may be consolidated automatically.</summary>
    public bool AutoLearn { get; init; } = true;
    /// <summary>Gets whether bounded read-only maintenance analysis may run in the background. Findings never authorize edits or discard knowledge.</summary>
    public bool MaintenanceEnabled { get; init; }
    /// <summary>Gets the maximum characters of recalled data per model turn.</summary>
    public int ContextCharacters { get; init; } = 5000;
    /// <summary>Gets the optional local BERT/WordPiece ONNX model.</summary>
    public string EmbeddingModel { get; init; } = "";
    /// <summary>Gets the matching local WordPiece vocabulary.</summary>
    public string EmbeddingVocabulary { get; init; } = "";
    /// <summary>Gets the lexical ranking weight.</summary>
    public double LexicalWeight { get; init; } = .45;
    /// <summary>Gets the neural cosine ranking weight.</summary>
    public double SemanticWeight { get; init; } = .3;
    /// <summary>Gets the explicit-neighbor ranking weight.</summary>
    public double GraphWeight { get; init; } = .15;
    /// <summary>Gets the recency ranking weight.</summary>
    public double TemporalWeight { get; init; } = .1;
    /// <summary>Gets the bounded graph render limit.</summary>
    public int GraphLimit { get; init; } = 400;
    /// <summary>Gets the maximum explicit-relation propagation depth; zero disables graph expansion.</summary>
    public int RetrievalGraphDepth { get; init; } = 2;
    /// <summary>Gets the maximum additional graph candidates per retrieval.</summary>
    public int RetrievalGraphCandidates { get; init; } = 160;
    /// <summary>Gets the graph traversal time budget, separate from native index/model calls.</summary>
    public int RetrievalGraphMilliseconds { get; init; } = 30;
    /// <summary>Gets local skill approvals pinned to exact file hashes; imports cannot grant trust by editing YAML.</summary>
    public IReadOnlyList<string> TrustedSkills { get; init; } = [];
    /// <summary>Bounds settings without silently changing the selected storage location.</summary>
    public BrainOptions Sanitized() => this with
    {
        Directory = Directory.Trim(), EmbeddingModel = EmbeddingModel.Trim(), EmbeddingVocabulary = EmbeddingVocabulary.Trim(),
        ContextCharacters = Math.Clamp(ContextCharacters, 500, 16000), GraphLimit = Math.Clamp(GraphLimit, 50, 1000),
        RetrievalGraphDepth = Math.Clamp(RetrievalGraphDepth, 0, 4), RetrievalGraphCandidates = Math.Clamp(RetrievalGraphCandidates, 8, 500),
        RetrievalGraphMilliseconds = Math.Clamp(RetrievalGraphMilliseconds, 5, 250),
        LexicalWeight = Weight(LexicalWeight, .45), SemanticWeight = Weight(SemanticWeight, .3),
        GraphWeight = Weight(GraphWeight, .15), TemporalWeight = Weight(TemporalWeight, .1)
    };
    /// <summary>Rejects nonfinite or negative ranking contributions.</summary>
    private static double Weight(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}

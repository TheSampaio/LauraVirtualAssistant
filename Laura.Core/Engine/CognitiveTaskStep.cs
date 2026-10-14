namespace Laura.Core.Engine;

/// <summary>One planned registered operation; its state changes only after an adapter result.</summary>
public sealed record CognitiveTaskStep(int Number, string Tool, string State);

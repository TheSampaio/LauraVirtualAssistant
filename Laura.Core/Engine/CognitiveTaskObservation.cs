namespace Laura.Core.Engine;

/// <summary>Bounded native output retained for a task continuation, separately from goal verification.</summary>
public sealed record CognitiveTaskObservation(int Call, string Tool, string Status, string Result, bool Truncated, string Initiator = "model");

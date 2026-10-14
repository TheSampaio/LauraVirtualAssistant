namespace Laura.Core.Engine;

/// <summary>Actual acquired source identity retained across bounded conversation continuations.</summary>
public sealed record CognitiveSourceEvidence(string Id, string Hash, string Path);

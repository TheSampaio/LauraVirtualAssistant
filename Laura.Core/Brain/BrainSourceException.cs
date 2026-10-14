namespace Laura.Core.Brain;

/// <summary>A safe native acquisition diagnostic, containing no remote error body or credentials.</summary>
public sealed class BrainSourceException(string message) : IOException(message);

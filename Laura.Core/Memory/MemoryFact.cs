namespace Laura.Core.Memory;

/// <summary>An explicit user memory, never an authority to run a tool.</summary>
/// <param name="Key">User-selected memory key.</param>
/// <param name="Text">Stored fact or preference.</param>
/// <param name="Updated">UTC time last updated.</param>
public sealed record MemoryFact(string Key, string Text, DateTimeOffset Updated);

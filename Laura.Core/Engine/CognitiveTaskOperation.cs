using System.Text.Json;

namespace Laura.Core.Engine;

/// <summary>One bounded operation selected from actual tool registration and source evidence, still dispatched through its native adapter.</summary>
public sealed record CognitiveTaskOperation(string Name, JsonElement Arguments);

using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Holds native protocol instructions and the exact currently exposed schemas, without any granted effect authority.</summary>
public sealed record LocalToolPrompt(string Instructions, IReadOnlyDictionary<string, JsonElement> Schemas);

using System.Text.Json;

namespace Laura.Core.ChatGpt;

/// <summary>Bounded provider input; no credentials, execution grants or hosted executors are accepted.</summary>
public sealed record ChatGptInferenceRequest(string Model, string Instructions, IReadOnlyList<JsonElement> Input,
    IReadOnlyList<JsonElement> Tools, bool IncludedPlanOnlyConfirmed);

/// <summary>One completed response with authoritative output items for stateless tool continuation; reasoning is never published.</summary>
public sealed record ChatGptInferenceResult(string Text, IReadOnlyList<JsonElement> Output, long? InputTokens, long? OutputTokens);

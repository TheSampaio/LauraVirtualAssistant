using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>One protocol-independent message; native model content preserves opaque reasoning signatures without displaying them.</summary>
public sealed record TextModelMessage(string Role, string Text, string CallId = "", string ToolName = "", JsonElement? Native = null, byte[]? Image = null, string MimeType = "image/png", CancellationToken ImageLifetime = default);
/// <summary>One inference request; all model tool declarations remain inert metadata.</summary>
public sealed record TextModelRequest(string Instructions, IReadOnlyList<TextModelMessage> Messages, object[] Declarations, int MaximumOutputTokens = 4096);
/// <summary>A model-selected request whose arguments must pass the shared native executor.</summary>
public sealed record TextModelCall(string Id, string Name, JsonElement Arguments);
/// <summary>A complete model round with visible text and optional native content for exact protocol continuation.</summary>
public sealed record TextModelReply(string Text, IReadOnlyList<TextModelCall> Calls, JsonElement Native, long? InputTokens = null, long? OutputTokens = null);

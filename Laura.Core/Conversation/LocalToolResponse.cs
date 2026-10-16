using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Contains only a validated call, final visible answer or a closed refusal; parsing grants no execution authority.</summary>
/// <param name="Kind">Closed protocol outcome.</param>
/// <param name="Answer">Final answer text, empty for a call or refusal.</param>
/// <param name="Name">Native registered name, empty unless a complete call was validated.</param>
/// <param name="Arguments">Owned JSON arguments, undefined unless a complete call was validated.</param>
public sealed record LocalToolResponse(LocalToolResponseKind Kind, string Answer = "", string Name = "", JsonElement Arguments = default);

namespace Laura.Core.ChatGpt;

/// <summary>Safe native connection metadata; no tokens, prompts or Brain content are included.</summary>
public sealed record ChatGptConnectionState(Guid? Active, IReadOnlyList<ChatGptAccount> Accounts, string Status,
    string Error = "", int? HttpStatus = null, string VerifiedModel = "");

/// <summary>A local registration label and verified identity display, distinct even for identical emails.</summary>
public sealed record ChatGptAccount(Guid Id, string Email, bool SignedIn, bool PlanAuthorized);

/// <summary>An account-specific catalog entry; listing does not prove successful inference.</summary>
public sealed record ChatGptModel(string Slug, string DisplayName);

/// <summary>Evidence from a fixed synthetic inference; usage is unavailable when the server omits it.</summary>
public sealed record ChatGptProbeResult(string Model, bool Completed, long? InputTokens, long? OutputTokens);

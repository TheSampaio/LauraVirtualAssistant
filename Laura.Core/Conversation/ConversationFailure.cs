using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Conversation;

/// <summary>Safe failure metadata; it never authorizes retries or retains remote payloads.</summary>
public sealed record ConversationFailure(ConversationFailureKind Kind, bool EffectsMayHaveRun = false, int? HttpStatus = null)
{
    /// <summary>Maps an HTTP status and optional structured error code without inspecting free-form messages.</summary>
    public static ConversationFailure FromHttp(int status, JsonElement error = default)
    {
        string? code = Field(error, "code"), type = Field(error, "type");
        ConversationFailureKind kind = code switch
        {
            "invalid_api_key" or "API_KEY_INVALID" or "API_KEY_EXPIRED" => ConversationFailureKind.Authentication,
            "insufficient_quota" or "billing_hard_limit_reached" or "organization_usage_limit_exceeded" or "billing_quota_exceeded" => ConversationFailureKind.UsageLimit,
            "rate_limit_exceeded" or "slow_down" => ConversationFailureKind.CapacityLimit,
            "model_not_found" => ConversationFailureKind.ModelUnavailable,
            "context_length_exceeded" => ConversationFailureKind.ContextExceeded,
            _ when type == "insufficient_quota" => ConversationFailureKind.UsageLimit,
            _ => status switch
            {
                401 => ConversationFailureKind.Authentication,
                403 => ConversationFailureKind.AccessDenied,
                404 => ConversationFailureKind.ModelUnavailable,
                408 or 504 => ConversationFailureKind.Timeout,
                429 => ConversationFailureKind.CapacityLimit,
                >= 500 and <= 599 => ConversationFailureKind.Unavailable,
                >= 300 and <= 499 => ConversationFailureKind.RequestRejected,
                _ => ConversationFailureKind.Unknown,
            },
        };
        return new(kind, HttpStatus: status == 0 ? null : status);
    }

    /// <summary>Returns localized, actionable wording and an explicit warning about uncertain prior effects.</summary>
    public string Describe(ILocalizer localizer)
    {
        string key = Kind switch
        {
            ConversationFailureKind.Authentication => LocalizationKeys.GenerativeAi.AuthenticationFailed,
            ConversationFailureKind.AccessDenied => LocalizationKeys.GenerativeAi.AccessDenied,
            ConversationFailureKind.UsageLimit => LocalizationKeys.GenerativeAi.UsageLimit,
            ConversationFailureKind.CapacityLimit => LocalizationKeys.GenerativeAi.CapacityLimit,
            ConversationFailureKind.ModelUnavailable => LocalizationKeys.GenerativeAi.ModelUnavailable,
            ConversationFailureKind.ContextExceeded => LocalizationKeys.GenerativeAi.ContextExceeded,
            ConversationFailureKind.Timeout => LocalizationKeys.GenerativeAi.Timeout,
            ConversationFailureKind.Network => LocalizationKeys.GenerativeAi.NetworkFailed,
            ConversationFailureKind.InvalidResponse => LocalizationKeys.GenerativeAi.InvalidResponse,
            ConversationFailureKind.Unavailable => LocalizationKeys.GenerativeAi.ServiceUnavailable,
            ConversationFailureKind.LocalIntegrity => LocalizationKeys.GenerativeAi.LocalIntegrity,
            ConversationFailureKind.InsufficientMemory => LocalizationKeys.GenerativeAi.InsufficientMemory,
            ConversationFailureKind.UnsupportedInput => LocalizationKeys.GenerativeAi.ImagesUnsupported,
            ConversationFailureKind.RequestRejected => LocalizationKeys.GenerativeAi.RequestRejected,
            ConversationFailureKind.OperationLimit => LocalizationKeys.GenerativeAi.OperationLimit,
            _ => LocalizationKeys.GenerativeAi.ProviderFailed,
        };
        string message = localizer.Get(key);
        return EffectsMayHaveRun ? message + " " + localizer.Get(LocalizationKeys.GenerativeAi.PriorEffects) : message;
    }

    /// <summary>Reads only bounded string codes from a structured object; arbitrary remote messages remain unread.</summary>
    private static string? Field(JsonElement error, string name) => error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: <= 96 } text ? text : null;
}

using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Bounds text-protocol responses before parsing and exposes only structured failure categories.</summary>
public static class TextModelHttp
{
    /// <summary>Reads at most eight MiB without retaining server error messages in exceptions. Args: response: HTTP response; token: Cancellation. Returns: Parsed JSON.</summary>
    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); byte[] buffer = new byte[16384]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > 8 * 1024 * 1024) { throw new ConversationProviderException(new(ConversationFailureKind.InvalidResponse)); }
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        JsonDocument document;
        try { document = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        { throw new ConversationProviderException(ConversationFailure.FromHttp((int)response.StatusCode)); }
        if (response.IsSuccessStatusCode) { return document; }
        JsonElement error = document.RootElement.TryGetProperty("error", out var nested) ? nested : document.RootElement;
        var failure = ConversationFailure.FromHttp((int)response.StatusCode, error);
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            && details.EnumerateArray().Take(8).Any(detail => detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String && reason.GetString() is "API_KEY_INVALID" or "API_KEY_EXPIRED"))
        { failure = failure with { Kind = ConversationFailureKind.Authentication }; }
        document.Dispose(); throw new ConversationProviderException(failure);
    }
}

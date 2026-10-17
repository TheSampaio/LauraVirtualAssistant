using System.Text;
using System.Text.Json;

namespace Laura.Core.ChatGpt;

/// <summary>Consumes SSE through an actual Responses terminal event; partial text never constitutes success.</summary>
public static class ChatGptResponseParser
{
    /// <summary>Reads bounded SSE including multiline data. Args: stream: HTTP response; model: Selected catalog slug; cancellationToken: Per-inference deadline. Returns: Verified completion and official usage, if supplied.</summary>
    public static async Task<ChatGptProbeResult> ReadProbeAsync(Stream stream, string model, CancellationToken cancellationToken)
    {
        var result = await ReadAsync(stream, null, cancellationToken).ConfigureAwait(false);
        return new(model, true, result.InputTokens, result.OutputTokens);
    }
    /// <summary>Reads one completed round and authoritative output items; publishes only provisional visible text deltas.</summary>
    public static async Task<ChatGptInferenceResult> ReadAsync(Stream stream, Action<string>? textDelta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var data = new StringBuilder(); var visible = new StringBuilder(); var completedItems = new SortedDictionary<int, JsonElement>(); long bytes = 0;
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is not null && (bytes += line.Length) > 2 * 1024 * 1024) { throw new InvalidDataException("response_stream_limit"); }
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0)
                {
                    string json = data.ToString(); data.Clear();
                    if (json.Trim() != "[DONE]")
                    {
                        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
                        var root = document.RootElement;
                        string type = root.TryGetProperty("type", out var kind) ? kind.GetString() ?? "" : "";
                        if (type == "response.output_text.delta" && root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                        { string text = delta.GetString() ?? ""; visible.Append(text); textDelta?.Invoke(text); }
                        if (type == "response.output_item.done" && root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                        {
                            int index = root.TryGetProperty("output_index", out var position) && position.ValueKind == JsonValueKind.Number && position.TryGetInt32(out int number) ? number : completedItems.Count;
                            if (index is < 0 or > 256 || completedItems.Count >= 256) { throw new InvalidDataException("response_stream_limit"); }
                            completedItems[index] = item.Clone();
                        }
                        if (type is "response.failed" or "response.incomplete" or "error") { throw new InvalidDataException(type.Replace('.', '_')); }
                        if (type == "response.completed")
                        {
                            if (!root.TryGetProperty("response", out var response) || !response.TryGetProperty("status", out var status) || status.GetString() != "completed")
                            { throw new InvalidDataException("response_terminal_invalid"); }
                            long? input = null, output = null;
                            if (response.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                            { input = Tokens(usage, "input_tokens"); output = Tokens(usage, "output_tokens"); }
                            JsonElement[] items = response.TryGetProperty("output", out var outputs) && outputs.ValueKind == JsonValueKind.Array
                                && outputs.GetArrayLength() > 0 ? outputs.EnumerateArray().Select(outputItem => outputItem.Clone()).ToArray() : completedItems.Values.ToArray();
                            Diagnostics.LauraDiagnostics.Record("chatgpt.response.output", amount: items.Length, outcome: "completed");
                            return new(visible.ToString(), items, input, output);
                        }
                    }
                }
                if (line is null) { throw new InvalidDataException("response_terminal_missing"); }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) { data.Append('\n'); }
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
    }
    /// <summary>Accepts only actual nonnegative integer counts supplied by the provider.</summary>
    private static long? Tokens(JsonElement usage, string name) => usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long count) && count >= 0 ? count : null;
}

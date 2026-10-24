using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Maps the shared text and tool protocol onto a private supervised llama.cpp endpoint.</summary>
public sealed class LlamaCppTextClient(LlamaCppRuntime runtime, HttpClient http, ISettingsService settings) : ITextModelClient
{
    /// <inheritdoc />
    public string Provider => "local";
    /// <inheritdoc />
    public string Model => Path.GetFileNameWithoutExtension(settings.Current.LocalModel.ModelPath);
    /// <inheritdoc />
    public bool IsAvailable => runtime.IsAvailable;
    /// <inheritdoc />
    public bool SupportsImages => false;
    /// <inheritdoc />
    public int ContextTokenLimit => settings.Current.LocalModel.ContextTokens;
    /// <inheritdoc />
    public async Task<TextModelReply> CompleteAsync(TextModelRequest request, CancellationToken token)
    {
        using var lease = await runtime.AcquireAsync(token).ConfigureAwait(false);
        var messages = new List<object> { new { role = "system", content = request.Instructions } };
        foreach (var item in request.Messages)
        {
            if (item.Image is not null) { throw new ConversationProviderException(new(ConversationFailureKind.UnsupportedInput)); }
            messages.Add(item.Native is { } native ? native : item.Role == "tool"
                ? new { role = "tool", content = item.Text, tool_call_id = item.CallId }
                : new { role = item.Role, content = item.Text });
        }
        var functions = request.Declarations.Select(group => JsonSerializer.SerializeToElement(group)).Where(group => group.TryGetProperty("functionDeclarations", out _))
            .SelectMany(group => group.GetProperty("functionDeclarations").EnumerateArray()).Select(function => new
        {
            type = "function", function = new { name = function.GetProperty("name").GetString(), description = function.GetProperty("description").GetString(),
                parameters = NormalizeSchema(JsonNode.Parse(function.GetProperty("parameters").GetRawText())!) }
        }).ToArray();
        using var template = await PostAsync(lease, "apply-template", new { messages, tools = functions, chat_template_kwargs = new { enable_thinking = false } }, token).ConfigureAwait(false);
        using var tokenized = await PostAsync(lease, "tokenize", new { content = template.RootElement.GetProperty("prompt").GetString(), add_special = true, parse_special = true }, token).ConfigureAwait(false);
        int inputTokens = tokenized.RootElement.GetProperty("tokens").GetArrayLength();
        int outputBudget = Math.Min(request.MaximumOutputTokens, settings.Current.LocalModel.ContextTokens - inputTokens - 64);
        if (outputBudget < 256) { throw new ConversationProviderException(new(ConversationFailureKind.ContextExceeded)); }
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(lease.Endpoint, "v1/chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.Key);
        message.Content = JsonContent.Create(new { model = "laura-local", messages, tools = functions, stream = false,
            max_tokens = outputBudget, temperature = .4,
            chat_template_kwargs = new { enable_thinking = false } });
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        using var data = await TextModelHttp.ReadAsync(response, token).ConfigureAwait(false);
        JsonElement root = data.RootElement, choice = root.GetProperty("choices")[0], output = choice.GetProperty("message");
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.GetString() == "length") { throw new ConversationProviderException(new(ConversationFailureKind.ContextExceeded)); }
        var calls = new List<TextModelCall>();
        if (output.TryGetProperty("tool_calls", out var selected))
        {
            foreach (var call in selected.EnumerateArray())
            {
                var function = call.GetProperty("function");
                using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "{}", new JsonDocumentOptions { MaxDepth = 16 });
                calls.Add(new(call.GetProperty("id").GetString() ?? "", function.GetProperty("name").GetString() ?? "", arguments.RootElement.Clone()));
            }
        }
        string visible = output.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
        long? input = null, result = null;
        if (root.TryGetProperty("usage", out var usage))
        { if (usage.TryGetProperty("prompt_tokens", out var count)) { input = count.GetInt64(); } if (usage.TryGetProperty("completion_tokens", out count)) { result = count.GetInt64(); } }
        return new(new VisibleModelOutput().Append(visible), calls, output.Clone(), input, result);
    }
    /// <summary>Uses the authenticated loopback runtime to measure its actual template/token budget without disclosing context elsewhere.</summary>
    private async Task<JsonDocument> PostAsync(LlamaCppRuntime.Lease lease, string path, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(lease.Endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.Key); request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        return await TextModelHttp.ReadAsync(response, token).ConfigureAwait(false);
    }
    /// <summary>Normalizes JSON schema type keywords without changing descriptions or literal property values.</summary>
    private static JsonNode NormalizeSchema(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            { if (pair.Key == "type" && pair.Value is JsonValue value && value.TryGetValue<string>(out string? type)) { obj[pair.Key] = type.ToLowerInvariant(); } else if (pair.Value is not null) { NormalizeSchema(pair.Value); } }
        }
        else if (node is JsonArray array) { foreach (var child in array) { if (child is not null) { NormalizeSchema(child); } } }
        return node;
    }
}

using System.Text;
using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Parses the reviewed Qwen3.5 function/parameter envelope as a complete structured response, never extracting commands from prose.</summary>
public static class LocalToolCallParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly LocalToolResponse Invalid = new(LocalToolResponseKind.Invalid);

    /// <summary>Parses a private agent response: one exact tool envelope or a closed JSON final-answer envelope. Prose never supplies an executable call.</summary>
    public static LocalToolResponse ParseAgent(string output, IReadOnlyDictionary<string, JsonElement> schemas)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(schemas);
        if (output.AsSpan().TrimStart().StartsWith("<tool_call>", StringComparison.Ordinal)) { return Parse(output, schemas); }
        try
        {
            if (output.Length > 65536 || StrictUtf8.GetByteCount(output) > 65536) { return Invalid; }
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2 }); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("answer", out var value)
                || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { return Invalid; }
            return new(LocalToolResponseKind.Answer, value.GetString()!);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or EncoderFallbackException) { return Invalid; }
    }

    /// <summary>Validates one whole call against native schemas. Args: output: Visible model response; schemas: Registered names mapped to native parameter schemas. Returns: An answer, one validated call, or a payload-free refusal.</summary>
    public static LocalToolResponse Parse(string output, IReadOnlyDictionary<string, JsonElement> schemas)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(schemas);
        if (output.Length is 0 or > 65536) { return Invalid; }
        try { if (StrictUtf8.GetByteCount(output) > 65536) { return Invalid; } }
        catch (EncoderFallbackException) { return Invalid; }
        ReadOnlySpan<char> text = output.AsSpan().Trim();
        if (!text.StartsWith("<tool_call>", StringComparison.Ordinal))
        {
            if (text.Length == 0 || text.Contains("<tool_call", StringComparison.Ordinal) || text.Contains("</tool_call", StringComparison.Ordinal)
                || text.Contains("<function=", StringComparison.Ordinal) || text.Contains("<parameter=", StringComparison.Ordinal)) { return Invalid; }
            return new(LocalToolResponseKind.Answer, output);
        }
        if (!Consume(ref text, "<tool_call>") || !TakeHeader(ref text, "<function=", out string name)
            || !ValidName(name) || !schemas.TryGetValue(name, out var schema)
            || schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var fields) || fields.ValueKind != JsonValueKind.Object) { return Invalid; }
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        while (text.StartsWith("<parameter=", StringComparison.Ordinal))
        {
            if (arguments.Count >= 64 || !TakeHeader(ref text, "<parameter=", out string parameter) || !ValidName(parameter)
                || arguments.ContainsKey(parameter) || !fields.TryGetProperty(parameter, out var definition)
                || !definition.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) { return Invalid; }
            // Only the envelope's own boundary newline is removed; literal argument whitespace is preserved.
            if (!ConsumeLineBreak(ref text)) { return Invalid; }
            int closing = text.IndexOf("</parameter>", StringComparison.Ordinal);
            if (closing < 1 || text[..closing].Contains("<tool_call", StringComparison.Ordinal) || text[..closing].Contains("<parameter=", StringComparison.Ordinal)) { return Invalid; }
            ReadOnlySpan<char> value = text[..closing];
            if (value.EndsWith("\r\n", StringComparison.Ordinal)) { value = value[..^2]; }
            else if (value.EndsWith("\n", StringComparison.Ordinal)) { value = value[..^1]; }
            else { return Invalid; }
            if (!ParseValue(value, type.GetString()!, out var argument)) { return Invalid; }
            arguments.Add(parameter, argument); text = text[(closing + "</parameter>".Length)..].TrimStart();
        }
        if (!Consume(ref text, "</function>") || !Consume(ref text, "</tool_call>") || !text.IsEmpty) { return Invalid; }
        try
        {
            JsonElement payload = JsonSerializer.SerializeToElement(arguments);
            if (StrictUtf8.GetByteCount(payload.GetRawText()) > 32768 || !ToolArgumentSchema.Validate(payload, schema)) { return Invalid; }
            return new(LocalToolResponseKind.Call, Name: name, Arguments: payload);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or EncoderFallbackException) { return Invalid; }
    }

    /// <summary>Reads a bounded name from the exact reviewed header without accepting attributes or nested syntax.</summary>
    private static bool TakeHeader(ref ReadOnlySpan<char> text, string prefix, out string name)
    {
        name = ""; if (!text.StartsWith(prefix, StringComparison.Ordinal)) { return false; }
        int end = text.IndexOf('>'); if (end < prefix.Length || end - prefix.Length > 64) { return false; }
        name = text[prefix.Length..end].ToString(); text = text[(end + 1)..];
        if (prefix == "<function=") { text = text.TrimStart(); } return true;
    }

    /// <summary>Consumes one exact structural token and only surrounding envelope whitespace.</summary>
    private static bool Consume(ref ReadOnlySpan<char> text, string token)
    {
        if (!text.StartsWith(token, StringComparison.Ordinal)) { return false; }
        text = text[token.Length..].TrimStart(); return true;
    }

    /// <summary>Consumes one protocol newline without trimming literal argument content.</summary>
    private static bool ConsumeLineBreak(ref ReadOnlySpan<char> text)
    {
        if (text.StartsWith("\r\n", StringComparison.Ordinal)) { text = text[2..]; return true; }
        if (text.StartsWith("\n", StringComparison.Ordinal)) { text = text[1..]; return true; } return false;
    }

    /// <summary>Accepts bounded native identifiers, never executable paths or shell fragments.</summary>
    private static bool ValidName(string name) => name.Length is > 0 and <= 64 && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    /// <summary>Converts typed parameter data into owned JSON without evaluating any literal content.</summary>
    private static bool ParseValue(ReadOnlySpan<char> value, string type, out JsonElement parsed)
    {
        parsed = default;
        if (type.Equals("STRING", StringComparison.OrdinalIgnoreCase)) { parsed = JsonSerializer.SerializeToElement(value.ToString()); return true; }
        string raw = value.ToString();
        if (type.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase) && raw is "True" or "False") { raw = raw.ToLowerInvariant(); }
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 12 }); parsed = document.RootElement.Clone(); return true;
        }
        catch (JsonException) { return false; }
    }
}

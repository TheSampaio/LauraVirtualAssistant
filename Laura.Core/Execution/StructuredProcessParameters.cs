using System.Text.Json;

namespace Laura.Core.Execution;

/// <summary>Reads actual structured process arguments without silently dropping model-supplied JSON values.</summary>
public static class StructuredProcessParameters
{
    /// <summary>Accepts a native string array or its legacy JSON string representation. Args: parameters: Tool object. Returns: Literal arguments. Raises: JsonException when a supplied value is malformed; only absence means no arguments.</summary>
    public static string[] Arguments(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("arguments", out var value)) { return []; }
        using var parsed = Parse(value);
        if (parsed.RootElement.ValueKind != JsonValueKind.Array || parsed.RootElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        { throw new JsonException("Process arguments must be a string array."); }
        return parsed.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    /// <summary>Accepts a string-valued environment object or its legacy JSON string representation. Returns: Case-insensitive explicit overrides. Raises: JsonException for malformed, duplicate or non-string fields.</summary>
    public static IReadOnlyDictionary<string, string> Environment(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("environment", out var value)) { return new Dictionary<string, string>(); }
        using var parsed = Parse(value);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) { throw new JsonException("Process environment must be an object."); }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in parsed.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || !result.TryAdd(property.Name, property.Value.GetString()!))
            { throw new JsonException("Environment overrides require unique string-valued fields."); }
        }
        return result;
    }

    /// <summary>Parses a bounded legacy JSON string once or snapshots an already typed JSON value.</summary>
    private static JsonDocument Parse(JsonElement value)
    {
        string text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        if (text.Length > 128000) { throw new JsonException("Process parameters exceed bounds."); }
        return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
    }
}

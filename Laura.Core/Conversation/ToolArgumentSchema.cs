using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Validates the bounded native function-schema subset without interpreting text as code or fetching schema references.</summary>
internal static class ToolArgumentSchema
{
    /// <summary>Checks native types, required fields, enums and explicit bounds; unsupported schema constructs fail closed.</summary>
    internal static bool Validate(JsonElement value, JsonElement schema, int depth = 0)
    {
        if (depth > 12 || schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || schema.EnumerateObject().Any(property => !SupportedKeyword(property.Name))) { return false; }
        if (schema.TryGetProperty("enum", out var choices) && (choices.ValueKind != JsonValueKind.Array
            || !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(value, choice)))) { return false; }
        return type.GetString()?.ToUpperInvariant() switch
        {
            "STRING" => value.ValueKind == JsonValueKind.String && LengthValid(value.GetString()!.Length, schema, "minLength", "maxLength"),
            "BOOLEAN" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "INTEGER" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _) && NumberValid(value, schema),
            "NUMBER" => value.ValueKind == JsonValueKind.Number && NumberValid(value, schema),
            "OBJECT" => ObjectValid(value, schema, depth),
            "ARRAY" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 256
                && LengthValid(value.GetArrayLength(), schema, "minItems", "maxItems") && schema.TryGetProperty("items", out var item)
                && value.EnumerateArray().All(element => Validate(element, item, depth + 1)),
            "NULL" => value.ValueKind == JsonValueKind.Null,
            _ => false,
        };
    }

    /// <summary>Allows only constraints implemented here and inert native catalogue annotations.</summary>
    private static bool SupportedKeyword(string name) => name is "type" or "description" or "title" or "enum" or "properties" or "required"
        or "items" or "minimum" or "maximum" or "minLength" or "maxLength" or "minItems" or "maxItems" or "additionalProperties";

    /// <summary>Checks unique object names and known properties recursively; unspecified extra properties are refused.</summary>
    private static bool ObjectValid(JsonElement value, JsonElement schema, int depth)
    {
        if (value.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) { return false; }
        if (schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind != JsonValueKind.False) { return false; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            if (!names.Add(field.Name) || names.Count > 64 || !properties.TryGetProperty(field.Name, out var definition)
                || !Validate(field.Value, definition, depth + 1)) { return false; }
        }
        if (!schema.TryGetProperty("required", out var required)) { return true; }
        return required.ValueKind == JsonValueKind.Array && required.EnumerateArray().All(name => name.ValueKind == JsonValueKind.String && names.Contains(name.GetString()!));
    }

    /// <summary>Applies finite numeric limits without changing the argument value.</summary>
    private static bool NumberValid(JsonElement value, JsonElement schema)
    {
        if (!value.TryGetDouble(out double number) || !double.IsFinite(number)) { return false; }
        foreach (string key in new[] { "minimum", "maximum" })
        {
            if (!schema.TryGetProperty(key, out var bound)) { continue; }
            if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetDouble(out double limit) || !double.IsFinite(limit)
                || key == "minimum" && number < limit || key == "maximum" && number > limit) { return false; }
        }
        return true;
    }

    /// <summary>Applies nonnegative schema length bounds; malformed limits cannot become permissive defaults.</summary>
    private static bool LengthValid(int length, JsonElement schema, string minimum, string maximum)
    {
        foreach (string key in new[] { minimum, maximum })
        {
            if (!schema.TryGetProperty(key, out var bound)) { continue; }
            if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetInt32(out int limit) || limit < 0
                || key == minimum && length < limit || key == maximum && length > limit) { return false; }
        }
        return true;
    }
}

using System.Text;
using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Constrains local decoding to native function/parameter syntax and structured final answers; parsing independently validates every complete response.</summary>
public static class LocalToolGrammar
{
    /// <summary>Builds bounded GBNF from native schemas. Args: schemas: Exact allowed names and parameter schemas. Returns: Grammar permitting one JSON answer or registered tool envelope. Raises: ArgumentException for unsafe names or excessive declarations.</summary>
    public static string Create(IReadOnlyDictionary<string, JsonElement> schemas)
    {
        if (schemas.Count > 128 || schemas.Keys.Any(name => name.Length is 0 or > 64 || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))))
        { throw new ArgumentException("Invalid native grammar function names.", nameof(schemas)); }
        var grammar = new StringBuilder("root ::= answer");
        for (int i = 0; i < schemas.Count; i++) { grammar.Append(" | call").Append(i); }
        grammar.AppendLine().AppendLine("answer ::= \"{\\\"answer\\\":\" ws json-string ws \"}\"");
        int index = 0;
        foreach (var (name, schema) in schemas)
        {
            if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var fields) || fields.ValueKind != JsonValueKind.Object
                || fields.EnumerateObject().Count() > 64 || fields.EnumerateObject().Any(field => !SafeName(field.Name)))
            { throw new ArgumentException("Invalid native grammar parameter schema.", nameof(schemas)); }
            var required = schema.TryGetProperty("required", out var list) ? list.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
            var rules = new List<string>();
            grammar.Append("call").Append(index).Append(" ::= ").Append(Literal("<tool_call>\n<function=" + name + ">\n"));
            int parameter = 0;
            foreach (var field in fields.EnumerateObject())
            {
                string rule = "p" + index + "x" + parameter++; grammar.Append(' ').Append(rule);
                if (!required.Contains(field.Name)) { grammar.Append('?'); }
                rules.Add(rule + " ::= " + Literal("<parameter=" + field.Name + ">\n") + " " + ValueRule(field.Value) + " " + Literal("\n</parameter>\n"));
            }
            grammar.Append(' ').AppendLine(Literal("</function>\n</tool_call>"));
            foreach (string rule in rules) { grammar.AppendLine(rule); } index++;
        }
        grammar.AppendLine("ws ::= [ \\t\\r\\n]*");
        grammar.AppendLine("integer ::= \"-\"? (\"0\" | [1-9] [0-9]*)");
        grammar.AppendLine("number ::= integer (\".\" [0-9]+)? ([eE] [+-]? [0-9]+)?");
        grammar.AppendLine("json-string ::= \"\\\"\" ([^\"\\\\\\x00-\\x1f] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F]{4}))* \"\\\"\"");
        grammar.AppendLine("json ::= object | array | json-string | number | \"true\" | \"false\" | \"null\"");
        grammar.AppendLine("object ::= \"{\" ws (json-string ws \":\" ws json ws (\",\" ws json-string ws \":\" ws json ws)*)? \"}\"");
        grammar.AppendLine("array ::= \"[\" ws (json ws (\",\" ws json ws)*)? \"]\"");
        // Reserve the closing delimiter while retaining literal code and markup in string values.
        grammar.AppendLine("xml-string ::= xml-unit* (\"<\" | \"</\" | \"</p\" | \"</pa\" | \"</par\" | \"</para\" | \"</param\" | \"</parame\" | \"</paramet\" | \"</paramete\" | \"</parameter\")?");
        grammar.AppendLine("xml-unit ::= [^<\\x00] | \"<\" [^/\\x00] | \"</\" [^p\\x00] | \"</p\" [^a\\x00] | \"</pa\" [^r\\x00] | \"</par\" [^a\\x00] | \"</para\" [^m\\x00] | \"</param\" [^e\\x00] | \"</parame\" [^t\\x00] | \"</paramet\" [^e\\x00] | \"</paramete\" [^r\\x00] | \"</parameter\" [^>\\x00]");
        if (grammar.Length > 131072) { throw new ArgumentException("Native grammar exceeds its supported bound.", nameof(schemas)); }
        return grammar.ToString();
    }

    /// <summary>Uses native enum literals and JSON types; the parser independently checks nested types, bounds and duplicates.</summary>
    private static string ValueRule(JsonElement definition)
    {
        string type = definition.GetProperty("type").GetString()!.ToUpperInvariant();
        if (definition.TryGetProperty("enum", out var choices))
        {
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() is 0 or > 256) { throw new ArgumentException("Invalid native grammar enum.", nameof(definition)); }
            return "(" + string.Join(" | ", choices.EnumerateArray().Select(choice => Literal(type == "STRING" ? choice.GetString()! : choice.GetRawText()))) + ")";
        }
        return type switch { "STRING" => "xml-string", "INTEGER" => "integer", "NUMBER" => "number", "OBJECT" => "object", "ARRAY" => "array",
            "BOOLEAN" => "(\"true\" | \"false\")", "NULL" => "\"null\"", _ => throw new ArgumentException("Unsupported native grammar type.", nameof(definition)) };
    }

    /// <summary>Encodes a grammar literal rather than interpolating unchecked syntax.</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    /// <summary>Accepts bounded native identifiers, excluding paths or grammar fragments.</summary>
    private static bool SafeName(string name) => name.Length is > 0 and <= 64 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
}

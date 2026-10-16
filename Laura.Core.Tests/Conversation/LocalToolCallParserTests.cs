using System.Text.Json;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks typed local calls and adversarial envelopes without dispatching any native operation.</summary>
public sealed class LocalToolCallParserTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> Schemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["probe"] = JsonSerializer.SerializeToElement(new { type = "OBJECT", properties = new
        {
            text = new { type = "STRING" }, count = new { type = "INTEGER", minimum = 1, maximum = 4 },
            enabled = new { type = "BOOLEAN" }, mode = new { type = "STRING", @enum = new[] { "safe", "test" } },
            data = new { type = "OBJECT", properties = new { items = new { type = "ARRAY", items = new { type = "INTEGER" }, maxItems = 3 } }, required = new[] { "items" } },
        }, required = new[] { "text" } }),
        ["empty"] = JsonSerializer.SerializeToElement(new { type = "OBJECT", properties = new { } }),
    };

    /// <summary>Preserves literal whitespace and typed values rather than evaluating or normalizing command text.</summary>
    [Fact]
    public void PreservesLiteralArgumentsAndValidatesNestedTypes()
    {
        string output = Call("text", "  $(Write-Output 'literal')\n ")
            .Replace("</function>", "<parameter=count>\n3\n</parameter>\n<parameter=enabled>\nTrue\n</parameter>\n<parameter=data>\n{\"items\":[1,2]}\n</parameter>\n</function>", StringComparison.Ordinal);
        var result = LocalToolCallParser.Parse(output, Schemas);
        Assert.Equal(LocalToolResponseKind.Call, result.Kind);
        Assert.Equal("probe", result.Name); Assert.Equal("  $(Write-Output 'literal')\n ", result.Arguments.GetProperty("text").GetString());
        Assert.Equal(3, result.Arguments.GetProperty("count").GetInt32()); Assert.True(result.Arguments.GetProperty("enabled").GetBoolean());
        Assert.Equal(2, result.Arguments.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    /// <summary>Rejects wrong schema types, unknown fields, missing required values and malformed recursive JSON.</summary>
    [Theory]
    [InlineData("count", "5")]
    [InlineData("count", "1.5")]
    [InlineData("count", "\"2\"")]
    [InlineData("count", "1e999")]
    [InlineData("enabled", "yes")]
    [InlineData("mode", "unsafe")]
    [InlineData("unknown", "anything")]
    [InlineData("data", "{\"items\":[1,2,3,4]}")]
    [InlineData("data", "{\"items\":[\"1\"]}")]
    [InlineData("data", "{\"items\":[],\"items\":[]}")]
    [InlineData("data", "{\"items\":[],\"extra\":true}")]
    [InlineData("data", "{}")]
    public void RejectsInvalidArguments(string parameter, string value)
    {
        string valid = Call("text", "original"); string additional = Call(parameter, value);
        string merged = valid.Replace("</function>", additional[(additional.IndexOf("<parameter=", StringComparison.Ordinal))..additional.IndexOf("</function>", StringComparison.Ordinal)] + "</function>", StringComparison.Ordinal);
        Assert.Equal(LocalToolResponseKind.Invalid, LocalToolCallParser.Parse(merged, Schemas).Kind);
    }

    /// <summary>Refuses extraction from prose, code fences, multiple calls and duplicate or ambiguous parameters.</summary>
    [Fact]
    public void RejectsAmbiguousEnvelopes()
    {
        string valid = Call("text", "literal");
        string[] invalid = ["I will do this.\n" + valid, "```xml\n" + valid + "\n```", valid + valid, valid + "done",
            valid.Replace("probe", "unknown", StringComparison.Ordinal), valid.Replace("<parameter=text>", "<parameter=text attr=x>", StringComparison.Ordinal),
            valid.Replace("</function>", "<parameter=text>\nsecond\n</parameter>\n</function>", StringComparison.Ordinal),
            valid.Replace("literal", "<parameter=text>", StringComparison.Ordinal), valid.Replace("literal\n", "literal", StringComparison.Ordinal),
            "<tool_call><function=probe></function></tool_call>", valid[..^3], "<function=probe>", new('a', 65537), "\ud800"];
        foreach (string output in invalid) { Assert.Equal(LocalToolResponseKind.Invalid, LocalToolCallParser.Parse(output, Schemas).Kind); }
        var empty = LocalToolCallParser.Parse("<tool_call>\n<function=empty>\n</function>\n</tool_call>", Schemas);
        Assert.Equal(LocalToolResponseKind.Call, empty.Kind); Assert.Empty(empty.Arguments.EnumerateObject());
        Assert.Equal(LocalToolResponseKind.Answer, LocalToolCallParser.Parse("Resposta normal.", Schemas).Kind);
    }

    /// <summary>Fails closed for unsupported schema constraints rather than silently ignoring restrictions.</summary>
    [Theory]
    [InlineData("pattern")]
    [InlineData("allOf")]
    [InlineData("$ref")]
    [InlineData("exclusiveMinimum")]
    public void RejectsUnsupportedConstraints(string keyword)
    {
        var schema = new Dictionary<string, object> { ["type"] = "OBJECT", ["properties"] = new { }, [keyword] = "unsupported" };
        var schemas = new Dictionary<string, JsonElement> { ["empty"] = JsonSerializer.SerializeToElement(schema) };
        Assert.Equal(LocalToolResponseKind.Invalid, LocalToolCallParser.Parse("<tool_call><function=empty></function></tool_call>", schemas).Kind);
    }

    /// <summary>Builds one synthetic function envelope with a literal parameter.</summary>
    private static string Call(string parameter, string value) => $"<tool_call>\n<function=probe>\n<parameter={parameter}>\n{value}\n</parameter>\n</function>\n</tool_call>";

    /// <summary>Distinguishes structured answer data from callable syntax and refuses ambiguous final envelopes.</summary>
    [Fact]
    public void PrivateAgentAnswersRequireOneClosedStringField()
    {
        string literal = "Example only: " + Call("text", "no effect");
        var answer = LocalToolCallParser.ParseAgent(JsonSerializer.Serialize(new { answer = literal }), Schemas);
        Assert.Equal(LocalToolResponseKind.Answer, answer.Kind); Assert.Equal(literal, answer.Answer);
        foreach (string malformed in new[] { "Unstructured prose", "{\"answer\":3}", "{\"answer\":\"\"}", "{\"answer\":\"a\",\"extra\":true}",
            "{\"answer\":\"a\",\"answer\":\"b\"}", "```json\n{\"answer\":\"a\"}\n```", "{\"answer\":\"a\"} trailing" })
        { Assert.Equal(LocalToolResponseKind.Invalid, LocalToolCallParser.ParseAgent(malformed, Schemas).Kind); }
    }
}

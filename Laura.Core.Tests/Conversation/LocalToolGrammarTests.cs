using System.Text.Json;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks that generated decoder constraints cannot introduce model-supplied function names or grammar syntax.</summary>
public sealed class LocalToolGrammarTests
{
    /// <summary>Uses only exact exposed names and removes call syntax when a native task has stopped.</summary>
    [Fact]
    public void OnlyExposedNamesEnterGrammar()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "OBJECT", properties = new { } });
        var schemas = new Dictionary<string, JsonElement> { ["read_sensor"] = schema, ["describe_tool"] = schema };
        string grammar = LocalToolGrammar.Create(schemas);
        Assert.Contains("root ::= answer | call0 | call1", grammar, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_name", grammar, StringComparison.Ordinal);
        string stopped = LocalToolGrammar.Create(new Dictionary<string, JsonElement>());
        Assert.DoesNotContain("call ::=", stopped, StringComparison.Ordinal); Assert.DoesNotContain("name ::=", stopped, StringComparison.Ordinal);
    }

    /// <summary>Rejects unbounded and grammar-injecting names before entering the native sampler.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("name\" | injected")]
    [InlineData("name\nroot ::= unsafe")]
    [InlineData("tool/path")]
    public void InvalidNativeNameCannotBecomeGrammar(string name)
    {
        Assert.Throws<ArgumentException>(() => LocalToolGrammar.Create(new Dictionary<string, JsonElement> { [name] = default }));
    }
}

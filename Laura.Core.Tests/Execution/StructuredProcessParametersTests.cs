using System.Text.Json;
using Laura.Core.Execution;

namespace Laura.Core.Tests.Execution;

/// <summary>Reproduces the real Gemini argument-shape failure and guards against silent effect substitutions.</summary>
public sealed class StructuredProcessParametersTests
{
    /// <summary>Preserves typed and legacy arrays and object overrides without shell interpretation.</summary>
    [Theory]
    [InlineData("{\"arguments\":[\"--version\",\"a b\",\"$()\"]}")]
    [InlineData("{\"arguments\":\"[\\\"--version\\\",\\\"a b\\\",\\\"$()\\\"]\"}")]
    public void ModelArrayIsNeverSilentlyReplacedWithEmptyArguments(string json)
    {
        using var input = JsonDocument.Parse(json);
        Assert.Equal(new[] { "--version", "a b", "$()" }, StructuredProcessParameters.Arguments(input.RootElement));
        using var environment = JsonDocument.Parse("{\"environment\":{\"TASK_VALUE\":\"ação\"}}");
        Assert.Equal("ação", StructuredProcessParameters.Environment(environment.RootElement)["TASK_VALUE"]);
    }

    /// <summary>Rejects malformed explicitly supplied fields rather than executing a different command with defaults.</summary>
    [Theory]
    [InlineData("{\"arguments\":null}")]
    [InlineData("{\"arguments\":\"--version\"}")]
    [InlineData("{\"arguments\":[12]}")]
    [InlineData("{\"environment\":{\"X\":1}}")]
    [InlineData("{\"environment\":{\"X\":\"1\",\"x\":\"2\"}}")]
    public void InvalidParametersCannotBecomeUnintendedEffects(string json)
    {
        using var input = JsonDocument.Parse(json);
        Assert.ThrowsAny<JsonException>(() => { StructuredProcessParameters.Arguments(input.RootElement); StructuredProcessParameters.Environment(input.RootElement); });
    }
}

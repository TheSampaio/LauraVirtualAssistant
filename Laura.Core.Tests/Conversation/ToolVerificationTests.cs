using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

public sealed class ToolVerificationTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(123, false, true)]
    [InlineData(123, true, false)]
    public void TechnicalAcceptanceDoesNotInventVerifiedExecution(int? processId, bool failed, bool expected)
    {
        var result = new AssistantToolResult("Synthetic outcome") { Launch = new(true, "synthetic", processId), Failed = failed };
        using var document = JsonDocument.Parse(GeminiLiveProtocol.ToolResult("id", "open_app", result));
        JsonElement response = document.RootElement.GetProperty("toolResponse").GetProperty("functionResponses")[0].GetProperty("response");
        Assert.Equal(expected, response.GetProperty("executionVerified").GetBoolean());
        Assert.False(response.GetProperty("goalVerified").GetBoolean());
    }
}

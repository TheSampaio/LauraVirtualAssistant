using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies closed failure classification without interpreting or exposing arbitrary provider messages.</summary>
public sealed class ConversationFailureTests
{
    [Theory]
    [InlineData(401, "{}", ConversationFailureKind.Authentication)]
    [InlineData(403, "{}", ConversationFailureKind.AccessDenied)]
    [InlineData(404, "{}", ConversationFailureKind.ModelUnavailable)]
    [InlineData(429, "{}", ConversationFailureKind.CapacityLimit)]
    [InlineData(429, "{\"code\":\"insufficient_quota\"}", ConversationFailureKind.UsageLimit)]
    [InlineData(429, "{\"code\":\"organization_usage_limit_exceeded\"}", ConversationFailureKind.UsageLimit)]
    [InlineData(429, "{\"type\":\"insufficient_quota\",\"code\":\"unrecognized\"}", ConversationFailureKind.UsageLimit)]
    [InlineData(400, "{\"code\":\"API_KEY_INVALID\"}", ConversationFailureKind.Authentication)]
    [InlineData(400, "{\"code\":\"context_length_exceeded\"}", ConversationFailureKind.ContextExceeded)]
    [InlineData(400, "{\"code\":false,\"message\":\"quota API key private secret\"}", ConversationFailureKind.RequestRejected)]
    [InlineData(503, "{}", ConversationFailureKind.Unavailable)]
    [InlineData(504, "{}", ConversationFailureKind.Timeout)]
    [InlineData(307, "{}", ConversationFailureKind.RequestRejected)]
    public void ClassifiesStructuredStatusWithoutGuessingFromMessages(int status, string json, ConversationFailureKind expected)
    {
        using var document = JsonDocument.Parse(json);
        var failure = ConversationFailure.FromHttp(status, document.RootElement);
        Assert.Equal(expected, failure.Kind); Assert.Equal(status, failure.HttpStatus);
        Assert.DoesNotContain("private", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UncertainEffectsRemainExplicitRegardlessOfFailureCategory()
    {
        var failure = new ConversationFailure(ConversationFailureKind.ContextExceeded, true);
        Assert.Equal("ui.ai.failure.context ui.ai.failure.priorEffects", failure.Describe(new StubLocalizer()));
        Assert.Equal("ui.ai.providerFailed", new ConversationFailure(ConversationFailureKind.Unknown).Describe(new StubLocalizer()));
    }
}

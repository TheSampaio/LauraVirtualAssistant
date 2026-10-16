using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks local preference, explicit selection and cloud privacy independently of internet connectivity.</summary>
public sealed class LocalFirstRoutingTests
{
    [Theory]
    [InlineData("auto", true, true, 1)]
    [InlineData("auto", false, true, 0)]
    [InlineData("local", true, true, 0)]
    [InlineData("auto", true, false, 0)]
    public async Task ExplicitImagesRouteOnlyBeforeInvocationWithOptInAndReviewedVision(string selected, bool allowCloud, bool verifiedVision, int cloudCalls)
    {
        var local = new RecordingLocalInferenceEngine();
        var gemini = new RecordingConversationEngine(true, "Visual answer") { SupportsImages = verifiedVision };
        var gpt = new RecordingConversationEngine(true, "GPT");
        var settings = new StubSettingsService(LauraSettings.Default with { GenerativeAi = new() { Provider = selected, AllowCloudFallback = allowCloud } });
        using var router = new ConversationProviderRouter(settings, gemini, gpt, local);
        var turn = new ConversationTurn("Explicit synthetic image", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), "Synthetic")
        { TurnId = Guid.NewGuid(), Image = [1, 2, 3] };
        string? answer = await router.AskAsync(turn);
        Assert.Equal(cloudCalls, gemini.CallCount); Assert.Equal(0, local.Calls); Assert.Equal(0, gpt.CallCount);
        if (cloudCalls == 1) { Assert.Equal(turn, gemini.LastTurn); Assert.Equal("Visual answer", answer); Assert.Null(router.LastFailure); }
        else { Assert.Null(answer); Assert.Equal(ConversationFailureKind.UnsupportedInput, router.LastFailure!.Kind); }
        Assert.Equal("local", router.ActiveProvider);
    }

    [Fact]
    public async Task LocalFailureNeverReplaysAnAdmittedTurnOnCloudEvenWithOptIn()
    {
        var local = new RecordingLocalInferenceEngine { Answer = null, LastFailure = new(ConversationFailureKind.InsufficientMemory) };
        var gemini = new RecordingConversationEngine(true, "cloud"); var gpt = new RecordingConversationEngine(true, "paid");
        var settings = new StubSettingsService(LauraSettings.Default with { GenerativeAi = new() { Provider = "auto", AllowCloudFallback = true } });
        using var router = new ConversationProviderRouter(settings, gemini, gpt, local);
        Assert.Null(await router.AskAsync(new("Synthetic", System.Globalization.CultureInfo.InvariantCulture, "Synthetic")));
        Assert.Equal(ConversationFailureKind.InsufficientMemory, router.LastFailure!.Kind);
        Assert.Equal(1, local.Calls); Assert.Equal(0, gemini.CallCount); Assert.Equal(0, gpt.CallCount);
    }
    [Theory]
    [InlineData("auto", true, false, "local")]
    [InlineData("auto", true, true, "local")]
    [InlineData("auto", false, false, "unavailable")]
    [InlineData("auto", false, true, "gemini")]
    [InlineData("gemini", true, false, "gemini")]
    [InlineData("openai", true, false, "openai")]
    [InlineData("local", false, true, "local")]
    public async Task ProviderSelectionNeverSilentlyUsesPaidGptOrUploadsWithoutPermission(string selected, bool installed, bool allowCloud, string expected)
    {
        var local = new RecordingLocalInferenceEngine { IsAvailable = installed }; var gemini = new RecordingConversationEngine(true, "Gemini"); var gpt = new RecordingConversationEngine(true, "GPT");
        var settings = new StubSettingsService(LauraSettings.Default with { GenerativeAi = new() { Provider = selected, AllowCloudFallback = allowCloud } });
        using var router = new ConversationProviderRouter(settings, gemini, gpt, local);
        Assert.Equal(expected, router.ActiveProvider);
        if (router.IsAvailable) { await router.AskAsync(new("Synthetic context", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), "Synthetic user")); }
        Assert.Equal(expected == "local" && installed ? 1 : 0, local.Calls);
        Assert.Equal(expected == "gemini" ? 1 : 0, gemini.CallCount); Assert.Equal(expected == "openai" ? 1 : 0, gpt.CallCount);
        Assert.Equal(expected != "unavailable" && !(expected == "local" && !installed), router.IsAvailable);
    }

    [Theory]
    [InlineData("o3")]
    [InlineData("ft:gpt-4.1:synthetic-org:custom:123")]
    public void LegitimateOpenAiIdentifiersSurviveSyntaxValidationWithoutInventingCapabilities(string model)
    {
        Assert.Equal(model, new GenerativeAiOptions { OpenAiModel = model }.Sanitized().OpenAiModel);
    }
}

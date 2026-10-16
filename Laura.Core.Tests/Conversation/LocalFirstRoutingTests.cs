using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks local preference, explicit selection and cloud privacy independently of internet connectivity.</summary>
public sealed class LocalFirstRoutingTests
{
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

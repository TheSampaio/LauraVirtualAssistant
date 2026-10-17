using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies explicit provider selection and structural automatic routing without keyword heuristics or error-based escalation.</summary>
public sealed class ProviderRoutingTests
{
    /// <summary>Manual choice always wins, even when a task has a complex structured demand.</summary>
    [Theory]
    [InlineData(ConversationProviderSelection.Gemini, "gemini")]
    [InlineData(ConversationProviderSelection.ChatGpt, "chatgpt")]
    public async Task ManualChoiceWins(ConversationProviderSelection choice, string expected)
    {
        var gemini = new RecordingConversationEngine(true, "gemini"); var chat = new RecordingConversationEngine(true, "chatgpt");
        using var router = new ConversationProviderRouter(Settings(choice, true), gemini, chat);
        Assert.Equal(expected, await router.AskAsync(Turn(CognitiveTaskDemand.Synthesis)));
    }
    /// <summary>Structured stages require explicit owner automatic-disclosure authorization.</summary>
    [Theory]
    [InlineData(false, CognitiveTaskDemand.Analysis, "gemini")]
    [InlineData(true, CognitiveTaskDemand.Routine, "gemini")]
    [InlineData(true, CognitiveTaskDemand.Analysis, "chatgpt")]
    [InlineData(true, CognitiveTaskDemand.Synthesis, "chatgpt")]
    public async Task AutomaticRoutingUsesAuthorizedNativeStage(bool allow, CognitiveTaskDemand demand, string expected)
    {
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, allow), new RecordingConversationEngine(true, "gemini"), new RecordingConversationEngine(true, "chatgpt"));
        Assert.Equal(expected, await router.AskAsync(Turn(demand)));
    }
    /// <summary>ChatGPT unavailability never changes explicit Gemini requests.</summary>
    [Fact]
    public async Task UnavailableChatGptDoesNotHarmGemini()
    {
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, true), new RecordingConversationEngine(true, "gemini"), new RecordingConversationEngine(false, null));
        Assert.Equal("gemini", await router.AskAsync(Turn(CognitiveTaskDemand.Analysis)));
    }
    /// <summary>Timeout is not evidence of cognitive incapacity and cannot trigger automatic retransmission.</summary>
    [Fact]
    public async Task TimeoutDoesNotSwitchProvidersOrRepeatEffects()
    {
        var chat = new RecordingConversationEngine(true, "chatgpt"); var gemini = new RecordingConversationEngine(true, null) { LastFailure = new(ConversationFailureKind.Timeout, true) };
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, true), gemini, chat);
        Assert.Null(await router.AskAsync(Turn(CognitiveTaskDemand.Routine))); Assert.Equal(0, chat.CallCount); Assert.True(router.LastFailure!.EffectsMayHaveRun);
    }
    /// <summary>Builds explicit routing policy without enabling credentials or external requests.</summary>
    private static StubSettingsService Settings(ConversationProviderSelection selection, bool allow) => new(LauraSettings.Default with
    { GenerativeAi = new() { Enabled = true }, ProviderSelection = selection, ChatGpt = new() { Enabled = true, ShareContext = true, IncludedPlanOnly = true, AllowAutomatic = allow } });
    /// <summary>Uses unchanged natural text across cases so only native structural signals determine routing.</summary>
    private static ConversationTurn Turn(CognitiveTaskDemand demand) => new("Complex programming architecture investigation synthesis debug.", CultureInfo.GetCultureInfo("en-US"), "Tester") { TaskDemand = demand };
}

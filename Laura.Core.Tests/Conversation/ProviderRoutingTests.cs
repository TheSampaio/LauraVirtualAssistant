using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies explicit provider selection and structural automatic routing without keyword heuristics or error-based escalation.</summary>
public sealed class ProviderRoutingTests
{
    /// <summary>A timeout recovery keeps its original provider even when the alternate provider is ready.</summary>
    [Theory]
    [InlineData("gemini")]
    [InlineData("chatgpt")]
    public async Task RecoveryPreservesProviderAndAddsSmallerStageGuidance(string prior)
    {
        var gemini = new RecordingConversationEngine(true, "gemini"); var chat = new RecordingConversationEngine(true, "chatgpt");
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, true), gemini, chat);
        var turn = Turn(CognitiveTaskDemand.Analysis) with { ContinuingTaskTurnId = Guid.NewGuid(),
            TaskRouting = new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.ModelTimeout, CognitiveRecoveryAction.SplitStep, prior) };
        Assert.Equal(prior, await router.AskAsync(turn));
        Assert.Equal(1, prior == "gemini" ? gemini.CallCount : chat.CallCount); Assert.Equal(0, prior == "gemini" ? chat.CallCount : gemini.CallCount);
        Assert.Contains("one smaller verifiable stage", (prior == "gemini" ? gemini : chat).LastTurn!.Prompt, StringComparison.Ordinal);
    }
    /// <summary>Revoking secondary disclosure prevents a pending secondary recovery from transmitting data to either provider automatically.</summary>
    [Fact]
    public async Task RecoveryCannotTransferRevokedConsent()
    {
        var gemini = new RecordingConversationEngine(true, "gemini"); var chat = new RecordingConversationEngine(true, "chatgpt");
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, false), gemini, chat);
        Assert.Null(await router.AskAsync(Turn(CognitiveTaskDemand.Routine) with { TaskRouting = new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.ContextLimit, CognitiveRecoveryAction.ReduceContext, "chatgpt") }));
        Assert.Equal(0, gemini.CallCount); Assert.Equal(0, chat.CallCount);
    }
    /// <summary>Initial primary unavailability can use the explicitly authorized optional provider without retrying a failed request.</summary>
    [Fact]
    public async Task AuthorizedSecondaryCanServeWhenPrimaryIsUnavailableBeforeDispatch()
    {
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic, true), new RecordingConversationEngine(false, null), new RecordingConversationEngine(true, "chatgpt"));
        Assert.True(router.IsAvailable); Assert.Equal("chatgpt", await router.AskAsync(Turn(CognitiveTaskDemand.Routine)));
    }
    /// <summary>Actual provider/model/reason metadata is committed before the provider receives its first request.</summary>
    [Fact]
    public async Task ActiveModelIsVisibleBeforeInferenceCompletes()
    {
        var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal); Guid id = Guid.NewGuid();
        await tasks.BeginCycleAsync(new(id, "Investigate a synthetic project."), new HashSet<string>(StringComparer.Ordinal) { "probe" }, new(), CancellationToken.None);
        var settings = Settings(ConversationProviderSelection.Automatic, true);
        await settings.UpdateAsync(settings.Current with { ChatGpt = settings.Current.ChatGpt with { Model = "synthetic-model" } });
        var chat = new RecordingConversationEngine(true, "chatgpt") { Respond = (_, _) =>
        {
            var active = tasks.Current(id)!.Rounds!;
            Assert.Equal("chatgpt", active.Provider); Assert.Equal("synthetic-model", active.Model); Assert.Equal("running", active.Outcome);
            Assert.Equal(CognitiveRoutingReason.ComplexPlan, active.RoutingReason); return Task.FromResult<string?>("chatgpt");
        } };
        using var router = new ConversationProviderRouter(settings, new RecordingConversationEngine(true, "gemini"), chat, tasks);
        Assert.Equal("chatgpt", await router.AskAsync(Turn(CognitiveTaskDemand.Planning) with { TurnId = id }));
    }
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

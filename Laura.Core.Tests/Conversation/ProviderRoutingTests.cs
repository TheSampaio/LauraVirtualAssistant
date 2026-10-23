using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Models;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies local and GPT routing, owner consent, and safe recovery.</summary>
public sealed class ProviderRoutingTests
{
    /// <summary>The Light choice starts Gemini only, even when the local runtime and GPT are available.</summary>
    [Fact]
    public async Task LightGeminiChoiceDoesNotLoadLocalOrGpt()
    {
        var local = new RecordingConversationEngine(true, "local");
        var chat = new RecordingConversationEngine(true, "chatgpt");
        var gemini = new RecordingConversationEngine(true, "gemini");
        var settings = Settings(ConversationProviderSelection.Automatic);
        await settings.UpdateAsync(settings.Current with { GenerativeAi = new() { Enabled = true, LightProvider = LightProviderSelection.Gemini } });
        using var router = new ConversationProviderRouter(settings, chat, local: local, gemini: gemini);
        Assert.Equal("gemini", await router.AskAsync(Turn(CognitiveTaskDemand.Routine)));
        Assert.Equal("gemini", router.ActiveProvider);
        Assert.Equal(1, gemini.CallCount);
        Assert.Equal(0, local.CallCount);
        Assert.Equal(0, chat.CallCount);
    }

    /// <summary>Manual Gemini selection respects the protected connection's enabled setting.</summary>
    [Fact]
    public async Task ManualGeminiRequiresEnabledConnection()
    {
        var gemini = new RecordingConversationEngine(true, "gemini");
        var settings = Settings(ConversationProviderSelection.Gemini);
        using var router = new ConversationProviderRouter(settings, gemini: gemini);
        Assert.Null(await router.AskAsync(Turn(CognitiveTaskDemand.Routine)));
        Assert.Equal(0, gemini.CallCount);
        await settings.UpdateAsync(settings.Current with { GenerativeAi = new() { Enabled = true } });
        Assert.Equal("gemini", await router.AskAsync(Turn(CognitiveTaskDemand.Routine)));
        Assert.Equal(1, gemini.CallCount);
    }

    /// <summary>Manual provider selection wins over a complex task demand.</summary>
    [Theory]
    [InlineData(ConversationProviderSelection.Local, "local")]
    [InlineData(ConversationProviderSelection.ChatGpt, "chatgpt")]
    public async Task ManualChoiceWins(ConversationProviderSelection choice, string expected)
    {
        var local = new RecordingConversationEngine(true, "local");
        var chat = new RecordingConversationEngine(true, "chatgpt");
        using var router = new ConversationProviderRouter(Settings(choice), chat, local: local);
        Assert.Equal(expected, await router.AskAsync(Turn(CognitiveTaskDemand.Analysis)));
        Assert.Equal(expected == "local" ? 1 : 0, local.CallCount);
        Assert.Equal(expected == "chatgpt" ? 1 : 0, chat.CallCount);
    }

    /// <summary>Automatic routine work uses the local model without cloud disclosure.</summary>
    [Fact]
    public async Task AutomaticRoutineUsesLocal()
    {
        var local = new RecordingConversationEngine(true, "local");
        var chat = new RecordingConversationEngine(true, "chatgpt");
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic), chat, local: local);
        Assert.Equal("local", await router.AskAsync(Turn(CognitiveTaskDemand.Routine)));
        Assert.Equal(0, chat.CallCount);
    }

    /// <summary>Verified wake facts stay local even when a fact happens to contain a complex-task keyword.</summary>
    [Fact]
    public async Task StartupBriefingUsesLocalModel()
    {
        var local = new RecordingConversationEngine(true, "Today's briefing");
        var chat = new RecordingConversationEngine(true, "cloud");
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic), chat, local: local);
        var turn = Turn(CognitiveTaskDemand.Analysis) with { Prompt = "Verified fact: architecture review at 14:00.", IsStartupBriefing = true };
        Assert.Equal("Today's briefing", await router.AskAsync(turn));
        Assert.Equal(0, chat.CallCount);
    }

    /// <summary>Authorized planning work uses GPT and records its model before inference.</summary>
    [Fact]
    public async Task AuthorizedPlanningUsesChatGpt()
    {
        using var tasks = new CognitiveTasks(new RecordingTaskJournal());
        Guid id = Guid.NewGuid();
        await tasks.BeginCycleAsync(new(id, "Plan a synthetic task."), new HashSet<string>(StringComparer.Ordinal), new(), CancellationToken.None);
        var settings = Settings(ConversationProviderSelection.Automatic);
        var chat = new RecordingConversationEngine(true, "chatgpt") { Respond = (_, _) =>
        {
            Assert.Equal("chatgpt", tasks.Current(id)!.Rounds!.Provider);
            Assert.Equal("synthetic-model", tasks.Current(id)!.Rounds!.Model);
            return Task.FromResult<string?>("chatgpt");
        } };
        using var router = new ConversationProviderRouter(settings, chat, tasks, new RecordingConversationEngine(true, "local"));
        Assert.Equal("chatgpt", await router.AskAsync(Turn(CognitiveTaskDemand.Planning) with { TurnId = id }));
    }

    /// <summary>Max requires a separate explicit authorization even when ordinary GPT routing is allowed.</summary>
    [Theory]
    [InlineData(false, ConversationFailureKind.MaxAuthorizationRequired, 0)]
    [InlineData(true, null, 1)]
    public async Task MaxRequiresAuthorization(bool allowMax, ConversationFailureKind? failure, int calls)
    {
        var settings = Settings(ConversationProviderSelection.Automatic);
        await settings.UpdateAsync(settings.Current with { ChatGpt = settings.Current.ChatGpt with { AllowMax = allowMax, MaxModel = allowMax ? "verified-max-model" : "" } });
        var chat = new RecordingConversationEngine(true, "max answer");
        using var router = new ConversationProviderRouter(settings, chat, local: new RecordingConversationEngine(true, "local"));
        var answer = await router.AskAsync(new ConversationTurn("Use modo max to analyze this fixture.", CultureInfo.GetCultureInfo("en-US"), "Tester")
        { TurnId = Guid.NewGuid(), TaskDemand = CognitiveTaskDemand.Analysis });
        Assert.Equal(allowMax ? "max answer" : null, answer);
        Assert.Equal(calls, chat.CallCount);
        Assert.Equal(failure, router.LastFailure?.Kind);
    }

    /// <summary>A failed Light request stays with the selected local provider even without effects or visible output.</summary>
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task SafeFallbackRespectsEffectsAndVisibleOutput(bool effects, bool visible, int cloudCalls)
    {
        var local = new RecordingConversationEngine(true, null)
        { LastFailure = new(ConversationFailureKind.Timeout, effects), HasProvisionalOutput = visible };
        var chat = new RecordingConversationEngine(true, "recovered");
        using var router = new ConversationProviderRouter(Settings(ConversationProviderSelection.Automatic), chat, local: local);
        await router.AskAsync(Turn(CognitiveTaskDemand.Routine));
        Assert.Equal(cloudCalls, chat.CallCount);
    }

    /// <summary>Creates settings with local inference and separately consented GPT.</summary>
    private static StubSettingsService Settings(ConversationProviderSelection selection) => new(LauraSettings.Default with
    {
        ProviderSelection = selection,
        LocalModel = new() { Enabled = true },
        GenerativeAi = new() { LightProvider = LightProviderSelection.Local },
        ChatGpt = new() { Enabled = true, AllowAutomatic = true, ShareContext = true, IncludedPlanOnly = true, Model = "synthetic-model" }
    });

    /// <summary>Creates a synthetic turn with an explicit native demand.</summary>
    private static ConversationTurn Turn(CognitiveTaskDemand demand) => new("Inspect synthetic request.", CultureInfo.GetCultureInfo("en-US"), "Tester")
    { TurnId = Guid.NewGuid(), TaskDemand = demand };
}

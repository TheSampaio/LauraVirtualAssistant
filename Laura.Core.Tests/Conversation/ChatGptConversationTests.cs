using System.Globalization;
using System.Text.Json;
using Laura.Core.ChatGpt;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies shared native tools, data-disclosure opt-in and stable task identities across independent ChatGPT rounds.</summary>
public sealed class ChatGptConversationTests
{
    /// <summary>Consent to one registration never enables a newly selected account or sends it saved context.</summary>
    [Fact]
    public async Task AccountSwitchRequiresMatchingNativeConsent()
    {
        var inference = new RecordingChatGptInference(); var settings = Settings(true);
        await settings.UpdateAsync(settings.Current with { ChatGpt = settings.Current.ChatGpt with { Registration = Guid.NewGuid() } });
        using var engine = new ChatGptConversationEngine(inference, settings, new WorkingConversationMemory(), new LocalAgentTools());
        Assert.False(engine.IsAvailable); Assert.Null(await engine.AskAsync(Turn())); Assert.Empty(inference.Requests);
    }
    /// <summary>Never sends saved dialogue, Brain data or tool definitions without recipient-specific owner opt-in.</summary>
    [Fact]
    public async Task IsolatesHistoryAndBrainWithoutDisclosureOptIn()
    {
        var inference = new RecordingChatGptInference(); var tools = new LocalAgentTools();
        using var engine = new ChatGptConversationEngine(inference, Settings(false), new WorkingConversationMemory(), tools);
        await engine.AskAsync(Turn() with { History = [new(ConversationMessageSource.UserTyped, "private previous dialogue", DateTimeOffset.UtcNow)], Memories = ["private fact"] });
        var request = Assert.Single(inference.Requests); Assert.Single(request.Input); Assert.Empty(request.Tools);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(request.Input), StringComparison.Ordinal);
    }
    /// <summary>Uses the same native adapter under the actual OpenAI recipient and carries outputs into a new stateless request.</summary>
    [Fact]
    public async Task DispatchesSharedNativeToolOnceAndPreservesTaskIdentity()
    {
        var inference = new RecordingChatGptInference(); inference.Results.Enqueue(Call("call_one"));
        var memory = new WorkingConversationMemory(); var tools = new LocalAgentTools(); var turn = Turn();
        using var engine = new ChatGptConversationEngine(inference, Settings(true), memory, tools);
        Assert.Equal("Synthetic completed response.", await engine.AskAsync(turn));
        Assert.Equal(1, tools.Calls); Assert.Equal("chatgpt", tools.ActiveProvider); Assert.Equal(turn.TurnId, memory.WorkingContext.TurnId);
        Assert.Equal(2, inference.Requests.Count);
        Assert.Contains(inference.Requests[1].Input, item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output");
        var space = Assert.Single(inference.Requests[0].Tools); Assert.Equal("namespace", space.GetProperty("type").GetString());
        Assert.Equal("object", space.GetProperty("tools")[0].GetProperty("parameters").GetProperty("type").GetString());
    }
    /// <summary>A repeated provider call identity cannot replay an already dispatched mutable effect.</summary>
    [Fact]
    public async Task RejectsDuplicateCallAfterActualDispatch()
    {
        var inference = new RecordingChatGptInference(); inference.Results.Enqueue(Call("same")); inference.Results.Enqueue(Call("same")); var tools = new LocalAgentTools();
        using var engine = new ChatGptConversationEngine(inference, Settings(true), new WorkingConversationMemory(), tools);
        Assert.Null(await engine.AskAsync(Turn())); Assert.Equal(1, tools.Calls); Assert.True(engine.LastFailure!.EffectsMayHaveRun);
    }
    /// <summary>Provider failure after a tool keeps the uncertain-effects indication and never repeats the tool.</summary>
    [Fact]
    public async Task FailureAfterToolDoesNotReplayEffects()
    {
        var inference = new RecordingChatGptInference(); int round = 0;
        inference.Execute = _ => ++round == 1 ? Task.FromResult(Call("one")) : Task.FromException<ChatGptInferenceResult>(new ChatGptConnectionException("subscription_sharing_usage_limit_exceeded", 429));
        var tools = new LocalAgentTools(); using var engine = new ChatGptConversationEngine(inference, Settings(true), new WorkingConversationMemory(), tools);
        Assert.Null(await engine.AskAsync(Turn())); Assert.Equal(1, tools.Calls);
        Assert.Equal(ConversationFailureKind.UsageLimit, engine.LastFailure!.Kind); Assert.True(engine.LastFailure.EffectsMayHaveRun);
    }
    /// <summary>Native refusal stops tool dispatch even when the provider produced more calls.</summary>
    [Fact]
    public async Task NativePermissionDenialStopsContinuation()
    {
        var inference = new RecordingChatGptInference(); inference.Results.Enqueue(Call("one")); var tools = new LocalAgentTools { Result = new("Denied") { Failed = true, Status = "blocked" } };
        using var engine = new ChatGptConversationEngine(inference, Settings(true), new WorkingConversationMemory(), tools);
        Assert.Null(await engine.AskAsync(Turn())); Assert.Equal(1, tools.Calls); Assert.Single(inference.Requests);
    }
    /// <summary>Owner resumption retains the original trusted task identity rather than adopting a new interaction identity.</summary>
    [Fact]
    public async Task ContinuationPreservesOriginalWorkingObjective()
    {
        var memory = new WorkingConversationMemory(); Guid task = Guid.NewGuid(); memory.SetWorkingContext(new ConversationWorkingContext(task, "original task"));
        using var engine = new ChatGptConversationEngine(new RecordingChatGptInference(), Settings(true), memory, new LocalAgentTools());
        await engine.AskAsync(Turn() with { ContinuingTaskTurnId = task }); Assert.Equal(task, memory.WorkingContext.TurnId); Assert.Equal("original task", memory.WorkingContext.UserText);
    }
    /// <summary>Owner cancellation propagates without manufacturing a provider answer.</summary>
    [Fact]
    public async Task CancellationStopsInference()
    {
        var inference = new RecordingChatGptInference { Execute = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new("", [], null, null); } };
        using var engine = new ChatGptConversationEngine(inference, Settings(true), new WorkingConversationMemory(), new LocalAgentTools());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.AskAsync(Turn(), cancellation.Token));
    }
    /// <summary>Bounds one scheduler cycle while retaining confirmed tool outputs for the next inference without replay.</summary>
    [Fact]
    public async Task ProtocolContinuationKeepsOutputsAndDoesNotRepeatTools()
    {
        var inference = new RecordingChatGptInference(); for (int index = 0; index < 4; index++) { inference.Results.Enqueue(Call("call" + index)); }
        var tools = new LocalAgentTools(); var memory = new WorkingConversationMemory(); var turn = Turn();
        using var engine = new ChatGptConversationEngine(inference, Settings(true), memory, tools, localizer: new StubLocalizer());
        Assert.Equal("ui.ai.taskCycleSaved", await engine.AskAsync(turn)); Assert.Equal(4, tools.Calls); Assert.NotNull(engine.PendingRound(turn.TurnId));
        Assert.Equal("Synthetic completed response.", await engine.AskAsync(turn with { TurnId = Guid.NewGuid(), ContinuingTaskTurnId = turn.TurnId }));
        Assert.Equal(4, tools.Calls); Assert.Null(engine.PendingRound(turn.TurnId));
        Assert.Equal(4, inference.Requests[^1].Input.Count(item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output"));
    }
    /// <summary>Builds an authorized synthetic configuration without selecting a real model.</summary>
    private static StubSettingsService Settings(bool share) => new(LauraSettings.Default with { ChatGpt = new() { Enabled = true, Model = "synthetic", ShareContext = share, IncludedPlanOnly = true } });
    /// <summary>Creates a trusted synthetic interaction identity.</summary>
    private static ConversationTurn Turn() => new("Inspect the synthetic fixture.", CultureInfo.GetCultureInfo("en-US"), "Tester") { TurnId = Guid.NewGuid() };
    /// <summary>Supplies a completed provider tool-call item without executing it in the fixture.</summary>
    private static ChatGptInferenceResult Call(string id) => new("", [JsonSerializer.SerializeToElement(new { type = "function_call", name = "probe", call_id = id, @namespace = "laura", arguments = "{\"text\":\"fixture\"}" })], 1, 1);
}

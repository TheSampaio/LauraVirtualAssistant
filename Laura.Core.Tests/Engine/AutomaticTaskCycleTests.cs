using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Engine;

/// <summary>Exercises automatic queued cycles through the real assistant, dispatcher, task coordinator and effect journal.</summary>
public sealed class AutomaticTaskCycleTests
{
    /// <summary>Routine dialogue beyond the operational retention bound cannot disable subsequent conversation or leave empty task rows.</summary>
    [Fact]
    public async Task RoutineDialogueDoesNotExhaustOperationalTaskRetention()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal);
        var memory = new WorkingConversationMemory(); var conversation = new RecordingConversationEngine(true, "A routine answer."); var tools = new LocalAgentTools();
        var settings = new StubSettingsService(LauraSettings.Default with { GreetOnStartup = false, GenerativeAi = new() { Enabled = true } });
        var localizer = new StubLocalizer(); var user = new StubUserContext(); var clock = new SystemClock();
        var dispatcher = new GeminiSkillDispatcher(conversation, settings, user, memory, localizer, tasks: tasks, tools: tools);
        await using var engine = new AssistantEngine(new RecordingSpeechSynthesizer(), dispatcher, settings, localizer, new StubShellController(),
            new GreetingComposer(localizer, user, clock), clock, NullLogger<AssistantEngine>.Instance, memory, tasks: tasks, taskTools: tools, conversation: conversation);
        await engine.StartAsync(deadline.Token);
        for (int turn = 0; turn < 205; turn++) { await engine.SubmitCommandAsync("A routine dialogue turn.", deadline.Token); }
        Assert.Equal(205, conversation.CallCount); Assert.Empty(await tasks.ListAsync(deadline.Token));
    }
    /// <summary>Executes a verified three-stage goal without repeated owner prompts, including recovery after a model timeout and interleaved owner dialogue.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AutomaticallyContinuesAndProcessesOwnerMessagesBetweenRounds(bool timeout, bool chatGpt)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var journal = new RecordingTaskJournal(); using var tasks = new CognitiveTasks(journal);
        var memory = new WorkingConversationMemory(); var conversation = new CyclingConversation(tasks, memory, timeout);
        var settings = new StubSettingsService(LauraSettings.Default with { GreetOnStartup = false, GenerativeAi = new() { Enabled = true },
            ProviderSelection = chatGpt ? ConversationProviderSelection.ChatGpt : ConversationProviderSelection.Gemini });
        var inactive = new RecordingConversationEngine(false, null);
        using var router = new Laura.Core.Conversation.ConversationProviderRouter(settings, chatGpt ? inactive : conversation, chatGpt ? conversation : inactive);
        var localizer = new StubLocalizer(); var user = new StubUserContext(); var clock = new SystemClock();
        var dispatcher = new GeminiSkillDispatcher(router, settings, user, memory, localizer, tasks: tasks, tools: conversation, routing: router);
        await using var engine = new AssistantEngine(new RecordingSpeechSynthesizer(), dispatcher, settings, localizer, new StubShellController(),
            new GreetingComposer(localizer, user, clock), clock, NullLogger<AssistantEngine>.Instance, memory, tasks: tasks, taskTools: conversation, conversation: router);
        await engine.StartAsync(deadline.Token);
        Task original = engine.SubmitCommandAsync("Perform the synthetic three-stage goal.", deadline.Token);
        await conversation.Started.Task.WaitAsync(deadline.Token);
        Task status = engine.SubmitCommandAsync("status", deadline.Token); conversation.Release.TrySetResult();
        await Task.WhenAll(original, status).WaitAsync(deadline.Token); await conversation.Finished.Task.WaitAsync(deadline.Token);
        await engine.SubmitCommandAsync("barrier", deadline.Token);
        Assert.Equal(["cycle1", "status", "cycle2", "cycle3", "barrier"], conversation.Order);
        var saved = (await journal.ReadAsync(conversation.Goal, deadline.Token))!;
        Assert.Equal(3, saved.Rounds!.Cycles); Assert.Equal(timeout ? 1 : 0, saved.Rounds.Recoveries);
        Assert.Equal(4, saved.Operations.Count); Assert.All(saved.Steps, step => Assert.Equal("completed", step.State));
        Assert.All(conversation.ContinuationProviders, provider => Assert.Equal(chatGpt ? "chatgpt" : "gemini", provider));
        Assert.Equal(3, engine.ConversationHistory.Count(message => message.Source == Laura.Core.Conversation.ConversationMessageSource.UserTyped));
        Assert.DoesNotContain(engine.ConversationHistory, message => message.Source == Laura.Core.Conversation.ConversationMessageSource.UserTyped && message.Text.Contains("Native continuation", StringComparison.Ordinal));
    }
}

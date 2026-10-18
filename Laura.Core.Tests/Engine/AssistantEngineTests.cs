using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Engine;

/// <summary>Verifies actual queued cancellation, worker recovery and concise internal briefing history.</summary>
public sealed class AssistantEngineTests
{
    /// <summary>Unavailable voice preserves the response and the next command remains usable.</summary>
    [Fact]
    public async Task UnavailableVoice_PreservesTextAndDoesNotFailTheCommand()
    {
        var speech = new RecordingSpeechSynthesizer { Available = false };
        var dispatcher = new CallbackSkillDispatcher((_, _) => Task.FromResult(SkillResponse.Speak("Verified text")));
        await using var engine = CreateEngine(dispatcher, speech);
        await engine.StartAsync();
        await engine.SubmitCommandAsync("first").WaitAsync(TimeSpan.FromSeconds(3));
        await engine.SubmitCommandAsync("second").WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, engine.ConversationHistory.Count(message => message.Source == ConversationMessageSource.Assistant && message.Text == "Verified text"));
        Assert.Equal(AssistantState.Ready, engine.State);
        Assert.Empty(speech.SpokenTexts);
    }

    /// <summary>Exercises real bounded admission, cancellation before dispatch and priority interruption of active/queued stale work.</summary>
    [Fact]
    public async Task BoundedQueueBackpressureDoesNotBlockCallerAndStopInterruptsActiveWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>();
        var dispatcher = new CallbackSkillDispatcher(async (request, token) =>
        {
            executed.Add(request.RawText);
            if (request.RawText == "long operation")
            { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } finally { if (token.IsCancellationRequested) { cancelled.SetResult(); } } }
            return SkillResponse.Silent();
        });
        await using var engine = CreateEngine(dispatcher, new RecordingSpeechSynthesizer()); await engine.StartAsync();
        Task active = engine.SubmitCommandAsync("long operation"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var queued = Enumerable.Range(0, 40).Select(index => engine.SubmitCommandAsync("queued " + index)).ToArray();
        Assert.Equal(32, engine.PendingMessageCount); Assert.All(queued, pending => Assert.False(pending.IsCompleted));
        using var admission = new CancellationTokenSource(); Task withdrawn = engine.SubmitCommandAsync("withdrawn before admission", admission.Token);
        admission.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withdrawn);
        engine.StopSpeaking(); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(queued.Append(active)).WaitAsync(TimeSpan.FromSeconds(5)); await engine.SubmitCommandAsync("new barrier");
        Assert.Equal(["long operation", "new barrier"], executed); Assert.Equal(0, engine.PendingMessageCount);
        Assert.Equal(["long operation", "new barrier"], engine.ConversationHistory.Select(message => message.Text));
    }

    [Theory]
    [InlineData("gemini")]
    public async Task GenerativeProcessingKeepsThinkingVisualAndSpeaksOnlyTheAnswer(string provider)
    {
        var speech = new RecordingSpeechSynthesizer(); var localizer = new StubLocalizer(); var clock = new SystemClock();
        var settings = new StubSettingsService(LauraSettings.Default with { GreetOnStartup = false, GenerativeAi = new() { Provider = provider, Enabled = true } });
        var states = new List<AssistantState>(); Guid turnId = Guid.Empty;
        var dispatcher = new CallbackSkillDispatcher((request, _) => { turnId = request.TurnId; return Task.FromResult(SkillResponse.Speak("Verified answer")); });
        await using var engine = new AssistantEngine(speech, dispatcher, settings, localizer, new StubShellController(),
            new GreetingComposer(localizer, new StubUserContext(), clock), clock, NullLogger<AssistantEngine>.Instance);
        engine.StateChanged += (_, state) => states.Add(state);
        await engine.StartAsync(); await engine.SubmitCommandAsync("What are you doing?");
        Assert.Equal(["Verified answer"], speech.SpokenTexts);
        Assert.Contains(AssistantState.Thinking, states); Assert.Equal(AssistantState.Ready, engine.State);
        Assert.NotEqual(Guid.Empty, turnId); Assert.Equal(turnId, engine.ConversationHistory[0].TurnId);
        Assert.Equal(turnId, engine.ConversationHistory[1].TurnId);
        Assert.Equal(["What are you doing?", "Verified answer"], engine.ConversationHistory.Select(message => message.Text));
    }

    [Fact]
    public async Task ClearDuringAnUncooperativeTurnWaitsAndFencesOldResponsesAndQueuedCommands()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>(); var speech = new RecordingSpeechSynthesizer();
        var dispatcher = new CallbackSkillDispatcher(async (request, _) =>
        {
            executed.Add(request.RawText);
            if (request.RawText == "old") { entered.SetResult(); await release.Task; return SkillResponse.Speak("late old answer"); }
            return SkillResponse.Silent();
        });
        await using var engine = CreateEngine(dispatcher, speech); await engine.StartAsync();
        Task old = engine.SubmitCommandAsync("old"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task queued = engine.SubmitCommandAsync("must never run"); Task clear = engine.ClearConversationAsync();
        Assert.False(clear.IsCompleted); release.SetResult();
        await Task.WhenAll(old, queued, clear).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(engine.ConversationHistory); Assert.Empty(speech.SpokenTexts); Assert.Equal(["old"], executed);
        await engine.SubmitCommandAsync("new"); Assert.Equal("new", Assert.Single(engine.ConversationHistory).Text);
    }
    [Fact]
    public async Task VisualTurnUsesTheExistingQueueWithFrozenPixelsAndTextOnlyHistory()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] pixels = [1, 2, 3, 4]; byte[]? seen = null;
        var dispatcher = new CallbackSkillDispatcher(async (request, token) =>
        {
            if (request.RawText == "first") { entered.SetResult(); await release.Task.WaitAsync(token); }
            else { Assert.Equal("Explain the selected image", request.RawText); seen = request.Image; }
            return SkillResponse.Silent();
        });
        await using var engine = CreateEngine(dispatcher, new RecordingSpeechSynthesizer()); await engine.StartAsync();
        Task first = engine.SubmitCommandAsync("first"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task visual = engine.SubmitVisualCommandAsync("Explain the selected image", pixels); pixels[0] = 99;
        release.SetResult(); await first; await visual.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, seen); Assert.NotSame(pixels, seen);
        Assert.Equal(new[] { "first", "Explain the selected image" }, engine.ConversationHistory.Select(message => message.Text));
    }
    [Fact]
    public async Task CancelingAnActiveBriefingCancelsDispatchAndAllowsTheNextCommand()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speech = new RecordingSpeechSynthesizer();
        var dispatcher = new CallbackSkillDispatcher(async (request, token) =>
        {
            if (request.RawText == "briefing")
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { if (token.IsCancellationRequested) { canceled.SetResult(); } }
            }
            return SkillResponse.Speak("next answer");
        });
        await using var engine = CreateEngine(dispatcher, speech);
        await engine.StartAsync();
        using var wake = new CancellationTokenSource();
        Task briefing = engine.SubmitContextCommandAsync("briefing", "Double clap", wake.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task next = engine.SubmitCommandAsync("next command");
        await wake.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => briefing);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await next.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(AssistantState.Ready, engine.State);
        Assert.Equal(["next answer"], speech.SpokenTexts);
    }

    [Fact]
    public async Task CancelingAQueuedWakeSkipsItsDispatchAndHistory()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new List<string>();
        var dispatcher = new CallbackSkillDispatcher(async (request, token) =>
        {
            commands.Add(request.RawText);
            if (request.RawText == "first") { entered.SetResult(); await release.Task.WaitAsync(token); }
            return SkillResponse.Silent();
        });
        await using var engine = CreateEngine(dispatcher, new RecordingSpeechSynthesizer());
        await engine.StartAsync();
        Task first = engine.SubmitCommandAsync("first");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var wake = new CancellationTokenSource();
        Task queued = engine.SubmitContextCommandAsync("internal briefing", "Double clap", wake.Token);
        await wake.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.SetResult(); await first.WaitAsync(TimeSpan.FromSeconds(3));
        await engine.SubmitCommandAsync("last").WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first", "last"], commands);
        Assert.DoesNotContain(engine.ConversationHistory, message => message.Text == "Double clap");
    }

    [Fact]
    public async Task ContextRequestShowsItsTitleWhileDispatchReceivesFactsAndThinkingState()
    {
        const string context = "Summarize the verified source JSON: {\"date\":\"2026-10-12\"}";
        string? dispatched = null;
        var states = new List<AssistantState>();
        var speech = new RecordingSpeechSynthesizer();
        var dispatcher = new CallbackSkillDispatcher((request, _) =>
        { Assert.Equal(SkillRequestSource.Context, request.Source); dispatched = request.RawText; return Task.FromResult(SkillResponse.Speak("brief answer")); });
        await using var engine = CreateEngine(dispatcher, speech, generativeEnabled: true);
        engine.StateChanged += (_, state) => states.Add(state);
        await engine.StartAsync();
        await engine.SubmitContextCommandAsync(context, "Double clap · daily briefing");
        Assert.Equal(context, dispatched);
        Assert.Equal(["Double clap · daily briefing", "brief answer"], engine.ConversationHistory.Select(message => message.Text));
        Assert.Contains(AssistantState.Thinking, states);
        Assert.Equal(AssistantState.Ready, engine.State);
    }

    [Fact]
    public async Task StartupCollectsTheBriefingOnceAndKeepsSourceJsonOutOfVisibleHistory()
    {
        var settings = new StubSettingsService(LauraSettings.Default with { GreetOnStartup = true }); var localizer = new StubLocalizer(); var clock = new SystemClock();
        var source = new StartupSource(); var briefing = new Laura.Core.Conversation.WakeBriefing([source], localizer, clock, NullLogger<Laura.Core.Conversation.WakeBriefing>.Instance);
        var speech = new RecordingSpeechSynthesizer(); int openings = 0;
        var dispatcher = new CallbackSkillDispatcher((request, _) =>
        {
            if (request.Source != SkillRequestSource.Context) { return Task.FromResult(SkillResponse.Silent()); }
            Assert.Contains("verified startup fixture", request.RawText, StringComparison.Ordinal); Assert.Contains("greeting", request.RawText, StringComparison.Ordinal); openings++;
            return Task.FromResult(SkillResponse.Speak("Synthetic opening and verified briefing."));
        });
        await using var engine = new AssistantEngine(speech, dispatcher, settings, localizer, new StubShellController(), new GreetingComposer(localizer, new StubUserContext(), clock), clock, NullLogger<AssistantEngine>.Instance, startupBriefing: briefing);
        await engine.StartAsync(); await engine.SubmitCommandAsync("barrier"); await engine.StartAsync();
        await settings.UpdateAsync(settings.Current with { UserNickname = "Synthetic owner" }); await engine.SubmitCommandAsync("second barrier");
        Assert.Equal(1, source.Calls); Assert.Equal(1, openings); Assert.Equal(["Synthetic opening and verified briefing."], speech.SpokenTexts);
        Assert.DoesNotContain(engine.ConversationHistory, message => message.Text.Contains("verified startup fixture", StringComparison.Ordinal)); Assert.Equal(AssistantState.Ready, engine.State);
    }

    [Fact]
    public async Task UnconfiguredStartupDefersItsOpeningUntilTheFirstAvailableSettingsChange()
    {
        var settings = new StubSettingsService(LauraSettings.Default with { GreetOnStartup = true }); var localizer = new StubLocalizer(); var clock = new SystemClock();
        var source = new StartupSource(); var briefing = new Laura.Core.Conversation.WakeBriefing([source], localizer, clock, NullLogger<Laura.Core.Conversation.WakeBriefing>.Instance);
        var dispatcher = new AvailabilityDispatcher(); var speech = new RecordingSpeechSynthesizer();
        await using var engine = new AssistantEngine(speech, dispatcher, settings, localizer, new StubShellController(), new GreetingComposer(localizer, new StubUserContext(), clock), clock, NullLogger<AssistantEngine>.Instance, startupBriefing: briefing);
        await engine.StartAsync(); await engine.SubmitCommandAsync("barrier"); Assert.Equal(0, source.Calls);
        dispatcher.Available = true; await settings.UpdateAsync(settings.Current with { UserNickname = "Synthetic" }); await dispatcher.Opened.Task.WaitAsync(TimeSpan.FromSeconds(3)); await engine.SubmitCommandAsync("barrier");
        Assert.Equal(1, source.Calls); Assert.Single(speech.SpokenTexts);
    }

    /// <summary>Provides verified synthetic facts without opening the microphone or network.</summary>
    private sealed class StartupSource : IBriefingContributor
    {
        public int Calls { get; private set; }
        /// <inheritdoc />
        public Task<Laura.Core.Conversation.BriefingSection?> CollectAsync(CancellationToken cancellationToken)
        { Calls++; return Task.FromResult<Laura.Core.Conversation.BriefingSection?>(new("news", "synthetic provider", DateTimeOffset.UtcNow, "verified startup fixture")); }
    }
    /// <summary>Models credential availability without a real Google key or connection.</summary>
    private sealed class AvailabilityDispatcher : ISkillDispatcher
    {
        public bool Available { get; set; }
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public bool CanUseGenerativeFallback(SkillRequest request) => Available;
        /// <inheritdoc />
        public Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default)
        { if (request.Source == SkillRequestSource.Context) { Opened.TrySetResult(); return Task.FromResult(SkillResponse.Speak("Synthetic first greeting")); } return Task.FromResult(SkillResponse.Silent()); }
    }
    /// <summary>Creates a domain engine with no platform, network, persistent memory or startup speech.</summary>
    private static AssistantEngine CreateEngine(ISkillDispatcher dispatcher, ISpeechSynthesizer speech, bool generativeEnabled = false)
    {
        var localizer = new StubLocalizer(); var clock = new SystemClock();
        return new(speech, dispatcher, new StubSettingsService(LauraSettings.Default with { GreetOnStartup = false, GenerativeAi = new() { Enabled = generativeEnabled } }),
            localizer, new StubShellController(), new GreetingComposer(localizer, new StubUserContext(), clock), clock,
            NullLogger<AssistantEngine>.Instance);
    }
}

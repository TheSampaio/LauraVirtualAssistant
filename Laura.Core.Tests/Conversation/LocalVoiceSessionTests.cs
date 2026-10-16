using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Speech;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks explicit local capture, cancellation and voice origin with the real queued assistant engine.</summary>
public sealed class LocalVoiceSessionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReleasedChordOrMutedSettingsCannotStartCapture(bool muted)
    {
        var settings = Settings(muted); var input = new Input(); using var released = new CancellationTokenSource(); released.Cancel();
        await using var engine = Engine(settings); await engine.StartAsync(); using var router = Router(settings);
        await using var session = new LocalVoiceSession(input, engine, settings, router);
        await session.StartAsync(false, released.Token); Assert.Equal(0, input.Calls); Assert.Empty(engine.ConversationHistory); Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task SilenceNeverDispatchesAndACompletedTranscriptPreservesItsVoiceOrigin()
    {
        var settings = Settings(); var input = new Input(); await using var engine = Engine(settings); await engine.StartAsync(); using var router = Router(settings);
        await using var session = new LocalVoiceSession(input, engine, settings, router);
        await session.StartAsync(false, CancellationToken.None); Assert.Empty(engine.ConversationHistory);
        input.Transcript = "Synthetic spoken command"; await session.StartAsync(false, CancellationToken.None);
        var user = Assert.Single(engine.ConversationHistory); Assert.Equal(ConversationMessageSource.UserVoice, user.Source); Assert.NotEqual(Guid.Empty, user.TurnId);
        Assert.Equal("pt-BR", input.Culture); Assert.False(session.IsCapturing); Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task ImmediateStopCancelsEvenBeforeTheWorkerStartsAndDoesNotDispatchACommand()
    {
        var settings = Settings(); var input = new Input { Wait = true }; await using var engine = Engine(settings); await engine.StartAsync(); using var router = Router(settings);
        await using var session = new LocalVoiceSession(input, engine, settings, router);
        Task active = session.StartAsync(true, CancellationToken.None); session.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active); Assert.Empty(engine.ConversationHistory); Assert.False(session.IsRunning); Assert.False(session.IsCapturing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MutingOrSwitchingProvidersClosesActiveLocalCaptureWithoutDispatch(bool mute)
    {
        var settings = Settings(); var input = new Input { Wait = true }; await using var engine = Engine(settings); await engine.StartAsync(); using var router = Router(settings);
        await using var session = new LocalVoiceSession(input, engine, settings, router);
        Task active = session.StartAsync(true, CancellationToken.None); await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await settings.UpdateAsync(mute ? settings.Current with { MicrophoneMuted = true }
            : settings.Current with { GenerativeAi = settings.Current.GenerativeAi with { Provider = "gemini" } });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.False(session.IsCapturing); Assert.False(session.IsRunning); Assert.Empty(engine.ConversationHistory);
    }

    [Theory]
    [InlineData("pt-BR", "Isso é tudo.")]
    [InlineData("en-US", "That's all.")]
    public async Task ExactStandbyEndsContinuousLocalListeningBeforeAnyModelInvocation(string culture, string transcript)
    {
        var settings = Settings(); var input = new Input { Transcript = transcript };
        var localizer = new Laura.Core.Localization.JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<Laura.Core.Localization.JsonLocalizer>.Instance);
        localizer.SetCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
        await using var engine = Engine(settings); await engine.StartAsync(); using var router = Router(settings);
        await using var session = new LocalVoiceSession(input, engine, settings, router, new ListeningControl(localizer));
        await session.StartAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, input.Calls); Assert.False(session.IsRunning); Assert.False(session.IsCapturing);
        Assert.DoesNotContain(engine.ConversationHistory, message => message.Source == ConversationMessageSource.UserVoice);
    }

    /// <summary>Creates explicitly unmuted synthetic settings with no cloud authorization or startup speech.</summary>
    private static StubSettingsService Settings(bool muted = false) => new(LauraSettings.Default with { MicrophoneMuted = muted, Culture = "pt-BR", GreetOnStartup = false, GenerativeAi = new() { Provider = "local" } });
    /// <summary>Creates the production queue around a silent deterministic skill.</summary>
    private static AssistantEngine Engine(ISettingsService settings)
    {
        var localizer = new StubLocalizer(); var clock = new SystemClock();
        return new(new RecordingSpeechSynthesizer(), new CallbackSkillDispatcher((_, _) => Task.FromResult(Laura.Core.Skills.SkillResponse.Silent())), settings, localizer,
            new StubShellController(), new GreetingComposer(localizer, new StubUserContext(), clock), clock, NullLogger<AssistantEngine>.Instance);
    }
    /// <summary>Routes only the available synthetic local backend.</summary>
    private static ConversationProviderRouter Router(ISettingsService settings) => new(settings, new RecordingConversationEngine(false, null), new RecordingConversationEngine(false, null), new RecordingLocalInferenceEngine());

    /// <summary>Models actual explicit capture without opening any device.</summary>
    private sealed class Input : ISpeechInput
    {
        internal int Calls { get; private set; }
        internal bool Wait { get; init; }
        internal string? Transcript { get; set; }
        internal string? Culture { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public IReadOnlyList<string> Languages => ["pt-BR", "en-US"];
        /// <inheritdoc />
        public IReadOnlyList<AudioDevice> InputDevices => [];
        /// <inheritdoc />
        public IReadOnlyList<AudioDevice> OutputDevices => [];
        /// <inheritdoc />
        public async Task<string?> RecognizeAsync(string culture, int device, CancellationToken stopRecording, CancellationToken cancellationToken)
        { Calls++; Culture = culture; Entered.TrySetResult(); if (Wait) { await Task.Delay(Timeout.Infinite, cancellationToken); } return Transcript; }
    }
}

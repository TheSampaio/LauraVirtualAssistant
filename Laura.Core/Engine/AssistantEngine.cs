using System.Threading.Channels;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Orchestrates Laura's text-command cycle: understand, act, and answer.
///
/// All work happens in a single consumer loop fed by a queue. UI calls only enqueue
/// messages, so the window stays responsive while Laura speaks or waits for a model.
/// </summary>
public sealed class AssistantEngine : IAssistantEngine
{
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ISkillDispatcher _dispatcher;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IShellController _shell;
    private readonly GreetingComposer _greetingComposer;
    private readonly IClock _clock;
    private readonly ILogger<AssistantEngine> _logger;

    private readonly Channel<EngineMessage> _messages = Channel.CreateUnbounded<EngineMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ConversationMessage> _conversationHistory = [];
    private readonly object _conversationHistoryGate = new();

    private const int MaxConversationHistory = 200;

    private Task? _worker;

    // Stored as an int because volatile reads of enums are not supported; the state
    // is read by the UI while the loop writes it.
    private int _state = (int)AssistantState.Stopped;

    /// <summary>
    /// Initializes the engine with its dependencies.
    /// </summary>
    /// <param name="synthesizer">Speech synthesis engine.</param>
    /// <param name="dispatcher">Skill dispatcher.</param>
    /// <param name="settings">Current settings and their change notifications.</param>
    /// <param name="localizer">Source of text in the active language.</param>
    /// <param name="shell">Visual layer control, used to shut down the application.</param>
    /// <param name="greetingComposer">Opening greeting composer.</param>
    /// <param name="clock">Source of date and time.</param>
    /// <param name="logger">Destination for diagnostic logs.</param>
    public AssistantEngine(
        ISpeechSynthesizer synthesizer,
        ISkillDispatcher dispatcher,
        ISettingsService settings,
        ILocalizer localizer,
        IShellController shell,
        GreetingComposer greetingComposer,
        IClock clock,
        ILogger<AssistantEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(greetingComposer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _synthesizer = synthesizer;
        _dispatcher = dispatcher;
        _settings = settings;
        _localizer = localizer;
        _shell = shell;
        _greetingComposer = greetingComposer;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<AssistantState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ConversationMessage>? ConversationMessageReceived;

    /// <inheritdoc />
    public AssistantState State => (AssistantState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public IReadOnlyList<ConversationMessage> ConversationHistory
    {
        get
        {
            lock (_conversationHistoryGate)
            {
                return [.. _conversationHistory];
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is not null)
        {
            return;
        }

        _settings.Changed += OnSettingsChanged;
        _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);

        LauraSettings settings = _settings.Current;
        _localizer.SetCulture(settings.ResolveCulture());

        if (settings.GreetOnStartup)
        {
            Enqueue(new SpeechRequested(_greetingComposer.ComposeStartupBriefing()));
        }

        await EnqueueAndWaitAsync(
            completion => new SettingsApplied(settings) { Completion = completion },
            cancellationToken).ConfigureAwait(false);

        SetState(AssistantState.Ready);
        _logger.LogInformation("Assistant engine started.");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is null)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;

        _synthesizer.CancelSpeech();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _messages.Writer.TryComplete();

        try
        {
            await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown must not hang because of the processing loop.
        }

        _worker = null;

        SetState(AssistantState.Stopped);
        _logger.LogInformation("Assistant engine stopped.");
    }

    /// <inheritdoc />
    public Task SubmitCommandAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return EnqueueAndWaitAsync(
            completion => new CommandSubmitted(text) { Completion = completion },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return EnqueueAndWaitAsync(
            completion => new SpeechRequested(text) { Completion = completion },
            cancellationToken);
    }

    /// <inheritdoc />
    public void StopSpeaking()
    {
        _synthesizer.CancelSpeech();
        Enqueue(new StopRequested());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetime.Dispose();
    }

    /// <summary>
    /// Consumes the message queue until the engine is stopped.
    /// </summary>
    /// <param name="cancellationToken">Token that ends the loop.</param>
    /// <returns>A task completed when the loop ends.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (EngineMessage message in _messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The assistant processing loop ended with a failure.");
        }
    }

    /// <summary>
    /// Handles a queue message, ensuring anyone waiting for it is released.
    /// </summary>
    /// <param name="message">Message to handle.</param>
    /// <param name="cancellationToken">Token that aborts handling.</param>
    /// <returns>A task completed when the message has been handled.</returns>
    private async Task HandleAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message)
            {
                case CommandSubmitted submitted:
                    PublishConversation(ConversationMessageSource.UserTyped, submitted.Text);
                    await ExecuteCommandAsync(submitted.Text, cancellationToken).ConfigureAwait(false);
                    break;

                case SpeechRequested requested:
                    await SpeakInternalAsync(requested.Text, cancellationToken).ConfigureAwait(false);
                    break;

                case SettingsApplied applied:
                    ApplySettings(applied.Settings);
                    break;

                case StopRequested:
                    StopSpeakingInternal();
                    break;

                default:
                    _logger.LogWarning("Unknown message in queue: {Message}.", message.GetType().Name);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle {Message}.", message.GetType().Name);
        }
        finally
        {
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Dispatches a typed command and speaks the response.
    /// </summary>
    /// <param name="text">Command text.</param>
    /// <param name="cancellationToken">Token that aborts execution.</param>
    /// <returns>A task completed when the response has finished being spoken.</returns>
    private async Task ExecuteCommandAsync(string text, CancellationToken cancellationToken)
    {
        SetState(AssistantState.Working);

        SkillRequest request = SkillRequest.Create(text, _localizer.Culture, _clock.Now, SkillRequestSource.Text);

        bool willUseGenerativeAi = _settings.Current.GenerativeAi.Enabled
            && _dispatcher.CanUseGenerativeFallback(request);

        if (willUseGenerativeAi)
        {
            await SpeakInternalAsync(_localizer.Get(LocalizationKeys.Assistant.Thinking), cancellationToken)
                .ConfigureAwait(false);
        }

        SkillResponse response = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        string? spokenText = response.Handled
            ? response.SpokenText
            : _localizer.Get(LocalizationKeys.Assistant.NotUnderstood);

        if (!string.IsNullOrWhiteSpace(spokenText))
        {
            await SpeakInternalAsync(spokenText, cancellationToken).ConfigureAwait(false);
        }

        if (response.RequestsShutdown)
        {
            _shell.Shutdown();
            return;
        }

        SetState(AssistantState.Ready);
    }

    /// <summary>
    /// Speaks text with the configured voice profile.
    /// </summary>
    /// <param name="text">Text to speak.</param>
    /// <param name="cancellationToken">Token that interrupts speech.</param>
    /// <returns>A task completed when speech ends.</returns>
    private async Task SpeakInternalAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AssistantState previousState = State;
        SetState(AssistantState.Speaking);

        try
        {
            LauraSettings settings = _settings.Current;
            var request = new SpeechRequest(text, settings.Voice, settings.Culture);

            PublishConversation(ConversationMessageSource.Assistant, text);
            await _synthesizer.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SetState(previousState);
        }
    }

    /// <summary>
    /// Cancels speech and returns the engine to ready state.
    /// </summary>
    private void StopSpeakingInternal()
    {
        _synthesizer.CancelSpeech();
        SetState(AssistantState.Ready);
    }

    /// <summary>
    /// Applies settings that affect engine-owned services.
    /// </summary>
    /// <param name="settings">Settings to apply.</param>
    private void ApplySettings(LauraSettings settings)
    {
        _localizer.SetCulture(settings.ResolveCulture());
        SetState(AssistantState.Ready);
    }

    /// <summary>
    /// Publishes a user or assistant message to subscribers and history.
    /// </summary>
    /// <param name="source">Message source.</param>
    /// <param name="text">Message text.</param>
    private void PublishConversation(ConversationMessageSource source, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new ConversationMessage(source, text.Trim(), _clock.Now);

        lock (_conversationHistoryGate)
        {
            _conversationHistory.Add(message);

            if (_conversationHistory.Count > MaxConversationHistory)
            {
                _conversationHistory.RemoveRange(0, _conversationHistory.Count - MaxConversationHistory);
            }
        }

        ConversationMessageReceived?.Invoke(this, message);
    }

    /// <summary>
    /// Publishes a new state, ignoring repeats.
    /// </summary>
    /// <param name="state">State to publish.</param>
    private void SetState(AssistantState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) == (int)state)
        {
            return;
        }

        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Enqueues a message, releasing anyone waiting if the queue is already closed.
    /// </summary>
    /// <param name="message">Message to enqueue.</param>
    private void Enqueue(EngineMessage message)
    {
        if (!_messages.Writer.TryWrite(message))
        {
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Enqueues a message and waits for the loop to process it.
    /// </summary>
    /// <param name="factory">Creates the message already associated with the completion signal.</param>
    /// <param name="cancellationToken">Token that aborts waiting.</param>
    /// <returns>A task completed when the message has been handled.</returns>
    private Task EnqueueAndWaitAsync(
        Func<TaskCompletionSource, EngineMessage> factory,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(factory(completion));
        return completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Forwards a settings change to the queue.
    /// </summary>
    /// <param name="sender">Settings service.</param>
    /// <param name="settings">Already-current settings.</param>
    private void OnSettingsChanged(object? sender, LauraSettings settings) =>
        Enqueue(new SettingsApplied(settings));

    /// <summary>
    /// Engine processing queue item.
    /// </summary>
    private abstract record EngineMessage
    {
        /// <summary>
        /// Gets the signal completed after handling, when someone is waiting for
        /// the message. Fire-and-forget messages leave it null.
        /// </summary>
        public TaskCompletionSource? Completion { get; init; }
    }

    /// <summary>Text command sent by the interface.</summary>
    private sealed record CommandSubmitted(string Text) : EngineMessage;

    /// <summary>Standalone speech request.</summary>
    private sealed record SpeechRequested(string Text) : EngineMessage;

    /// <summary>Settings to apply to the language.</summary>
    private sealed record SettingsApplied(LauraSettings Settings) : EngineMessage;

    /// <summary>Request to stop speech immediately.</summary>
    private sealed record StopRequested : EngineMessage;
}

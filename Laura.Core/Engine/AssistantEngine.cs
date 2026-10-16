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
    private readonly IConversationMemory? _memory;
    private readonly WakeBriefing? _startupBriefing;
    private CancellationTokenSource? _startupCancellation;
    private int _startupScheduled;
    private bool _operational;
    private readonly object _generationGate = new();
    private CancellationTokenSource _conversationCancellation = new();
    private long _conversationGeneration;
    private readonly AsyncLocal<long?> _activeGeneration = new();

    private readonly Channel<EngineMessage> _messages = Channel.CreateUnbounded<EngineMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ConversationMessage> _conversationHistory = [];
    private readonly object _conversationHistoryGate = new();

    private const int MaxConversationHistory = 200;

    private Task? _worker;
    private Guid _activeTurnId;

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
    /// <param name="memory">Optional unified persistent history.</param>
    /// <param name="startupBriefing">Optional verified weather, news, currency and reminder sources for the first opening in this process.</param>
    public AssistantEngine(
        ISpeechSynthesizer synthesizer,
        ISkillDispatcher dispatcher,
        ISettingsService settings,
        ILocalizer localizer,
        IShellController shell,
        GreetingComposer greetingComposer,
        IClock clock,
        ILogger<AssistantEngine> logger,
        IConversationMemory? memory = null, WakeBriefing? startupBriefing = null)
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
        _memory = memory;
        _startupBriefing = startupBriefing;
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
        if (_memory is not null && _settings.Current.RememberConversations)
        {
            try
            {
                IReadOnlyList<ConversationMessage> history = await Task.Run(() => _memory.ReadRecent(MaxConversationHistory), cancellationToken).ConfigureAwait(false);
                lock (_conversationHistoryGate) { _conversationHistory.AddRange(history); }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Persistent memory unavailable; continuing with session history.");
            }
        }
        _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);

        LauraSettings settings = _settings.Current;
        _localizer.SetCulture(settings.ResolveCulture());

        await EnqueueAndWaitAsync(
            completion => new SettingsApplied(settings) { Completion = completion },
            cancellationToken).ConfigureAwait(false);

        SetState(AssistantState.Ready);
        _operational = true;
        ScheduleStartup(settings);
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
            completion => new CommandSubmitted(text) { Completion = completion, RequestCancellation = cancellationToken },
            cancellationToken);
    }
    /// <inheritdoc />
    public Task SubmitVoiceCommandAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return EnqueueAndWaitAsync(completion => new CommandSubmitted(text) { Completion = completion,
            RequestCancellation = cancellationToken, MessageSource = ConversationMessageSource.UserVoice }, cancellationToken);
    }
    /// <inheritdoc />
    public Task ClearConversationAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is null || State == AssistantState.Stopped) { throw new InvalidOperationException("Start the assistant before resetting its conversation."); }
        lock (_generationGate)
        {
            Interlocked.Increment(ref _conversationGeneration);
            _conversationCancellation.Cancel(); _conversationCancellation.Dispose(); _conversationCancellation = new();
            _synthesizer.CancelSpeech();
            return EnqueueAndWaitAsync(completion => new ConversationCleared { Completion = completion }, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return EnqueueAndWaitAsync(
            completion => new SpeechRequested(text) { Completion = completion, RequestCancellation = cancellationToken },
            cancellationToken);
    }
    /// <inheritdoc />
    public Task SubmitContextCommandAsync(string text, string displayText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        return EnqueueAndWaitAsync(completion => new CommandSubmitted(text)
            { Completion = completion, RequestCancellation = cancellationToken, DisplayText = displayText }, cancellationToken);
    }
    /// <inheritdoc />
    public Task SubmitVisualCommandAsync(string text, byte[] png, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(png); if (png.Length is 0 or > 32 * 1024 * 1024) { throw new ArgumentException("Invalid visual attachment.", nameof(png)); }
        byte[] snapshot = (byte[])png.Clone();
        return EnqueueAndWaitAsync(completion => new CommandSubmitted(text) { Image = snapshot, Completion = completion, RequestCancellation = cancellationToken }, cancellationToken);
    }

    /// <inheritdoc />
    public void StopSpeaking()
    {
        try { _startupCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        _synthesizer.CancelSpeech();
        Enqueue(new StopRequested());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetime.Dispose();
        _conversationCancellation.Dispose();
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
            CancellationTokenSource request;
            lock (_generationGate)
            {
                if (message is CommandSubmitted or SpeechRequested or StartupRequested && message.Generation != _conversationGeneration) { return; }
                request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, message.RequestCancellation, _conversationCancellation.Token);
                _activeGeneration.Value = message.Generation;
            }
            using var requestLifetime = request;
            request.Token.ThrowIfCancellationRequested();
            switch (message)
            {
                case ConversationCleared:
                    if (_memory is not null) { _memory.ClearHistory(); }
                    lock (_conversationHistoryGate) { _conversationHistory.Clear(); }
                    Laura.Core.Diagnostics.LauraDiagnostics.Record("conversation.cleared");
                    SetState(AssistantState.Ready);
                    break;
                case CommandSubmitted submitted:
                    Guid turnId = Guid.NewGuid();
                    _activeTurnId = turnId;
                    PublishConversation(submitted.MessageSource, submitted.DisplayText ?? submitted.Text, turnId);
                    await ExecuteCommandAsync(submitted.Text,
                        submitted.DisplayText is null ? SkillRequestSource.Text : SkillRequestSource.Context, request.Token, submitted.Image, turnId).ConfigureAwait(false);
                    break;

                case SpeechRequested requested:
                    await SpeakInternalAsync(requested.Text, request.Token).ConfigureAwait(false);
                    break;

                case StartupRequested:
                    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(request.Token))
                    {
                        _startupCancellation = startup;
                        try
                        {
                            if (_startupBriefing is null) { await SpeakInternalAsync(_greetingComposer.ComposeStartupBriefing(), startup.Token).ConfigureAwait(false); }
                            else
                            {
                                SetState(AssistantState.Thinking);
                                string prompt = await _startupBriefing.CreateRequestAsync(_greetingComposer.ComposeStartupBriefing(), startup.Token).ConfigureAwait(false);
                                Guid startupTurnId = Guid.NewGuid();
                                _activeTurnId = startupTurnId;
                                PublishConversation(ConversationMessageSource.UserTyped, _startupBriefing.DisplayTitle, startupTurnId);
                                await ExecuteCommandAsync(prompt, SkillRequestSource.Context, startup.Token, turnId: startupTurnId).ConfigureAwait(false);
                            }
                        }
                        finally { _startupCancellation = null; SetState(AssistantState.Ready); }
                    }
                    break;

                case SettingsApplied applied:
                    ApplySettings(applied.Settings);
                    if (_operational) { ScheduleStartup(applied.Settings); }
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
            if (cancellationToken.IsCancellationRequested) { throw; }
            SetState(AssistantState.Ready);
        }
        catch (Exception exception)
        {
            if (message is ConversationCleared) { message.Completion?.TrySetException(exception); }
            _logger.LogError(exception, "Failed to handle {Message}.", message.GetType().Name);
            SetState(AssistantState.Ready);
        }
        finally
        {
            _activeGeneration.Value = null;
            _activeTurnId = Guid.Empty;
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Dispatches a typed command and speaks the response.
    /// </summary>
    /// <param name="text">Command text.</param>
    /// <param name="cancellationToken">Token that aborts execution.</param>
    /// <param name="source">Distinguishes an actionable command from non-actionable briefing data.</param>
    /// <param name="image">Explicit PNG attachment for this interaction, or null for text-only input.</param>
    /// <param name="turnId">Stable identity shared with the published user message.</param>
    /// <returns>A task completed when the response has finished being spoken.</returns>
    private async Task ExecuteCommandAsync(string text, SkillRequestSource source, CancellationToken cancellationToken, byte[]? image = null, Guid turnId = default)
    {
        SetState(AssistantState.Working);

        SkillRequest request = SkillRequest.Create(text, _localizer.Culture, _clock.Now, source) with { Image = image, TurnId = turnId };

        bool willUseGenerativeAi = _settings.Current.GenerativeAi.Enabled
            && _dispatcher.CanUseGenerativeFallback(request);
        if (willUseGenerativeAi) { SetState(AssistantState.Thinking); }

        SkillResponse response = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
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
            var request = new SpeechRequest(text, settings.Voice, settings.Culture) { OutputDevice = settings.OutputDevice };

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
    /// <param name="turnId">Originating turn identity, or empty for standalone notifications.</param>
    private void PublishConversation(ConversationMessageSource source, string text, Guid turnId = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_activeGeneration.Value is { } active && active != Volatile.Read(ref _conversationGeneration)) { return; }
        var message = new ConversationMessage(source, text, _clock.Now) { TurnId = turnId == Guid.Empty && source == ConversationMessageSource.Assistant ? _activeTurnId : turnId };
        Laura.Core.Diagnostics.LauraDiagnostics.Record("conversation.message", amount: text.Length, outcome: source.ToString(), interactionId: _memory?.WorkingTurnId);
        if (_memory is not null && _settings.Current.RememberConversations)
        {
            try { _memory.Append(message); }
            catch (Exception exception) { _logger.LogWarning(exception, "Could not persist conversation message."); }
        }

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
        Laura.Core.Diagnostics.LauraDiagnostics.Record("engine.state", outcome: state.ToString());
    }

    /// <summary>
    /// Enqueues a message, releasing anyone waiting if the queue is already closed.
    /// </summary>
    /// <param name="message">Message to enqueue.</param>
    private void Enqueue(EngineMessage message)
    {
        lock (_generationGate)
        {
        message.Generation = _conversationGeneration;
        if (!_messages.Writer.TryWrite(message))
        {
            message.Completion?.TrySetResult();
        }
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

    /// <summary>Queues one startup opening after configuration, deferring until Gemini is available on a fresh installation.</summary>
    private void ScheduleStartup(LauraSettings settings)
    {
        if (!settings.GreetOnStartup || (_startupBriefing is not null && !_dispatcher.CanUseGenerativeFallback(SkillRequest.Create("", settings.ResolveCulture(), _clock.Now, SkillRequestSource.Context)))) { return; }
        if (Interlocked.CompareExchange(ref _startupScheduled, 1, 0) == 0) { Enqueue(new StartupRequested()); }
    }

    /// <summary>
    /// Engine processing queue item.
    /// </summary>
    private abstract record EngineMessage
    {
        public long Generation { get; set; }
        /// <summary>
        /// Gets the signal completed after handling, when someone is waiting for
        /// the message. Fire-and-forget messages leave it null.
        /// </summary>
        public TaskCompletionSource? Completion { get; init; }
        /// <summary>Gets explicit cancellation for this command or speech request.</summary>
        public CancellationToken RequestCancellation { get; init; }
    }

    /// <summary>Text command sent by the interface.</summary>
    private sealed record CommandSubmitted(string Text) : EngineMessage
    {
        /// <summary>Gets the trusted capture origin selected by the native caller.</summary>
        public ConversationMessageSource MessageSource { get; init; } = ConversationMessageSource.UserTyped;
        /// <summary>Gets an explicitly queued image; saved history contains only its question and answer.</summary>
        public byte[]? Image { get; init; }
        /// <summary>Gets a concise user-visible label for a context-rich internal request.</summary>
        public string? DisplayText { get; init; }
    }

    /// <summary>Standalone speech request.</summary>
    private sealed record SpeechRequested(string Text) : EngineMessage;

    /// <summary>First configured startup opening; subsequent settings changes cannot repeat it.</summary>
    private sealed record StartupRequested : EngineMessage;

    /// <summary>Settings to apply to the language.</summary>
    private sealed record SettingsApplied(LauraSettings Settings) : EngineMessage;

    /// <summary>Request to stop speech immediately.</summary>
    private sealed record StopRequested : EngineMessage;
    private sealed record ConversationCleared : EngineMessage;
}

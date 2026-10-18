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
    private readonly CognitiveTasks? _tasks;
    private readonly IAssistantTools? _taskTools;
    private readonly IConversationEngine? _conversation;
    private CancellationTokenSource? _startupCancellation;
    private int _startupScheduled;
    private bool _operational;
    private readonly object _generationGate = new();
    private CancellationTokenSource _conversationCancellation = new();
    private CancellationTokenSource? _activeRequest;
    private long _conversationGeneration;
    private readonly AsyncLocal<long?> _activeGeneration = new();

    private readonly Channel<EngineMessage> _messages = Channel.CreateBounded<EngineMessage>(
        new BoundedChannelOptions(32) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });

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
    /// <param name="tasks">Optional private task checkpoint coordinator for explicit native resumption.</param>
    /// <param name="taskTools">Optional shared native tools for evidence-based cycle continuation.</param>
    /// <param name="conversation">Optional active provider used to classify a completed cycle.</param>
    public AssistantEngine(
        ISpeechSynthesizer synthesizer,
        ISkillDispatcher dispatcher,
        ISettingsService settings,
        ILocalizer localizer,
        IShellController shell,
        GreetingComposer greetingComposer,
        IClock clock,
        ILogger<AssistantEngine> logger,
        IConversationMemory? memory = null, WakeBriefing? startupBriefing = null, CognitiveTasks? tasks = null,
        IAssistantTools? taskTools = null, IConversationEngine? conversation = null)
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
        _taskTools = taskTools; _conversation = conversation;
        _dispatcher = dispatcher;
        _settings = settings;
        _localizer = localizer;
        _shell = shell;
        _greetingComposer = greetingComposer;
        _clock = clock;
        _logger = logger;
        _memory = memory;
        _startupBriefing = startupBriefing;
        _tasks = tasks;
    }

    /// <inheritdoc />
    public event EventHandler<AssistantState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ConversationMessage>? ConversationMessageReceived;

    /// <inheritdoc />
    public AssistantState State => (AssistantState)Volatile.Read(ref _state);
    /// <inheritdoc />
    public int PendingMessageCount => _messages.Reader.Count;

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
    public async Task ResumeTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks is null) { throw new InvalidOperationException("Task recovery is unavailable."); }
        var checkpoint = await _tasks.PrepareResumeAsync(taskId, cancellationToken).ConfigureAwait(false);
        await EnqueueAndWaitAsync(completion => new CommandSubmitted(checkpoint.ResumePrompt())
        { Completion = completion, RequestCancellation = cancellationToken, DisplayText = checkpoint.Objective, ContinuingTaskTurnId = taskId }, cancellationToken).ConfigureAwait(false);
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
        if (_startupCancellation is { } startup) { _ = CancelRequestAsync(startup); }
        CancellationTokenSource? active;
        lock (_generationGate) { Interlocked.Increment(ref _conversationGeneration); active = _activeRequest; }
        if (active is not null) { _ = CancelRequestAsync(active); }
        _synthesizer.CancelSpeech();
        Enqueue(new StopRequested());
    }

    /// <summary>Cancels callbacks asynchronously so an external cancellation registration cannot block the native UI.</summary>
    private async Task CancelRequestAsync(CancellationTokenSource request)
    {
        try { await request.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        catch (AggregateException exception) { _logger.LogWarning("Request cancellation callback failed ({ErrorType}).", exception.GetType().Name); }
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
        CancellationTokenSource? request = null;
        try
        {
            lock (_generationGate)
            {
                if (message is CommandSubmitted or SpeechRequested or StartupRequested && message.Generation != _conversationGeneration) { return; }
                request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, message.RequestCancellation, _conversationCancellation.Token);
                _activeRequest = request;
                _activeGeneration.Value = message.Generation;
            }
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
                    if (submitted.Automatic && _tasks?.Current(submitted.ContinuingTaskTurnId)?.Rounds is { CanContinue: false }) { break; }
                    Guid turnId = Guid.NewGuid();
                    _activeTurnId = turnId;
                    if (!submitted.Automatic) { PublishConversation(submitted.MessageSource, submitted.DisplayText ?? submitted.Text, turnId); }
                    if (submitted.ContinuingTaskTurnId != Guid.Empty && _tasks?.Current(submitted.ContinuingTaskTurnId) is { } resumed)
                    { _memory?.SetWorkingContext(new ConversationWorkingContext(resumed.Id, resumed.Objective)); }
                    await ExecuteCommandAsync(submitted.Text,
                        submitted.DisplayText is null ? SkillRequestSource.Text : SkillRequestSource.Context, request.Token, submitted.Image, turnId, submitted.ContinuingTaskTurnId).ConfigureAwait(false);
                    await ScheduleContinuationAsync(submitted.ContinuingTaskTurnId != Guid.Empty ? submitted.ContinuingTaskTurnId : turnId, request.Token).ConfigureAwait(false);
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
            lock (_generationGate) { if (ReferenceEquals(_activeRequest, request)) { _activeRequest = null; } }
            request?.Dispose();
            _activeGeneration.Value = null;
            _activeTurnId = Guid.Empty;
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>Queues one verified remaining stage at the tail so new owner messages can run between bounded model rounds.</summary>
    private async Task ScheduleContinuationAsync(Guid taskId, CancellationToken token)
    {
        if (_tasks is null || _taskTools is null || _conversation is null || _tasks.Current(taskId) is not { Rounds: { } rounds } checkpoint
            || rounds.Paused || rounds.Canceled || checkpoint.HasUncertainEffect || checkpoint.State == "blocked"
            || _conversation.LastFailure is { Kind: not (ConversationFailureKind.Timeout or ConversationFailureKind.ContextExceeded) }) { return; }
        using var recipient = new ProviderExecutionScope(rounds.Provider);
        if (!rounds.CanContinue)
        {
            string? remaining = _conversation.PendingRound(taskId) ?? await _tasks.InspectAsync(taskId, task => task.PendingContinuation(), token).ConfigureAwait(false);
            await _tasks.SetContinuationAsync(taskId, remaining is not null, token).ConfigureAwait(false); return;
        }
        string? pending = _conversation.PendingRound(taskId) ?? await _taskTools.PendingTaskContinuationAsync(token).ConfigureAwait(false);
        if (pending is null)
        { await _tasks.SetContinuationAsync(taskId, false, token).ConfigureAwait(false); await _tasks.RetireDialogueAsync(taskId, token).ConfigureAwait(false); return; }
        if (!await _tasks.SetContinuationAsync(taskId, true, token).ConfigureAwait(false)) { return; }
        var next = new CommandSubmitted(pending) { ContinuingTaskTurnId = taskId, Automatic = true, Generation = Volatile.Read(ref _conversationGeneration) };
        bool queued = _messages.Writer.TryWrite(next);
        Diagnostics.LauraDiagnostics.Record("task.continuation", outcome: queued ? "queued" : "queue_full_paused", taskId: taskId);
    }

    /// <summary>
    /// Dispatches a typed command and speaks the response.
    /// </summary>
    /// <param name="text">Command text.</param>
    /// <param name="cancellationToken">Token that aborts execution.</param>
    /// <param name="source">Distinguishes an actionable command from non-actionable briefing data.</param>
    /// <param name="image">Explicit PNG attachment for this interaction, or null for text-only input.</param>
    /// <param name="turnId">Stable identity shared with the published user message.</param>
    /// <param name="continuingTaskTurnId">Original task identity selected by the native resume action.</param>
    /// <returns>A task completed when the response has finished being spoken.</returns>
    private async Task ExecuteCommandAsync(string text, SkillRequestSource source, CancellationToken cancellationToken, byte[]? image = null, Guid turnId = default, Guid continuingTaskTurnId = default)
    {
        SetState(AssistantState.Working);

        SkillRequest request = SkillRequest.Create(text, _localizer.Culture, _clock.Now, source) with { Image = image, TurnId = turnId, ContinuingTaskTurnId = continuingTaskTurnId };

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
        catch (SpeechUnavailableException)
        {
            // Keep the verified text response; an unavailable voice is not a failed objective.
            _logger.LogWarning("Voice unavailable; the text response remains available.");
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
            if (_messages.Writer.TryWrite(message)) { return; }
        }
        _ = WriteMessageAsync(message, _lifetime.Token);
    }

    /// <summary>Waits for bounded queue capacity asynchronously, preserving caller cancellation and observable admission latency.</summary>
    private async Task WriteMessageAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await _messages.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            Laura.Core.Diagnostics.LauraDiagnostics.Record("engine.queue_wait", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, PendingMessageCount);
        }
        catch (OperationCanceledException) { message.Completion?.TrySetCanceled(cancellationToken); }
        catch (ChannelClosedException) { message.Completion?.TrySetCanceled(new CancellationToken(true)); }
    }

    /// <summary>
    /// Enqueues a message and waits for the loop to process it.
    /// </summary>
    /// <param name="factory">Creates the message already associated with the completion signal.</param>
    /// <param name="cancellationToken">Token that aborts waiting.</param>
    /// <returns>A task completed when the message has been handled.</returns>
    private async Task EnqueueAndWaitAsync(
        Func<TaskCompletionSource, EngineMessage> factory,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var message = factory(completion);
        lock (_generationGate) { message.Generation = _conversationGeneration; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await WriteMessageAsync(message, operation.Token).ConfigureAwait(false);
        await completion.Task.WaitAsync(operation.Token).ConfigureAwait(false);
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
        /// <summary>Gets whether native remaining work, rather than another owner message, scheduled this cycle.</summary>
        public bool Automatic { get; init; }
        /// <summary>Gets a checkpoint selected by the native resume action, never parsed from model text.</summary>
        public Guid ContinuingTaskTurnId { get; init; }
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

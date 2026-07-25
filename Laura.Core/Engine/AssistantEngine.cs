using System.Threading.Channels;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Laura.Core.Speech;
using Laura.Core.Text;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Orchestrates the assistant's full cycle: listen, understand, act, and answer.
///
/// All work happens in a single consumer loop fed by a queue.
/// Escolha deliberada: os eventos do reconhecedor chegam em threads do motor de
/// audio and the interface calls the engine from the UI thread - enqueuing instead of
/// running on the caller keeps both free while Laura listens or speaks, and
/// avoids locks for protecting state, since only the loop modifies it.
/// </summary>
public sealed class AssistantEngine : IAssistantEngine
{
    private readonly ISpeechRecognizer _recognizer;
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

    // How many times in a row Laura re-listens after failing to understand before
    // giving up, so a persistent misunderstanding does not loop forever.
    private const int MaxCommandRetries = 2;

    private Task? _worker;
    private bool _recognizerRunning;
    private long _listeningEpoch;
    private int _commandRetries;

    // Stored as an int because volatile reads of enums are not supported; the state
    // is read by the UI while the loop writes it.
    private int _state = (int)AssistantState.Stopped;

    /// <summary>
    /// Initializes the engine with its dependencies.
    ///
    /// Args:
    ///     recognizer: Motor de reconhecimento de fala.
    ///     synthesizer: Speech synthesis engine.
    ///     dispatcher: Despachante de habilidades.
    ///     settings: Current settings and their change notifications.
    ///     localizer: Source of text in the active language.
    ///     shell: Visual layer control, used to shut down the application.
    ///     greetingComposer: Opening greeting composer.
    ///     clock: Fonte de data e hora.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public AssistantEngine(
        ISpeechRecognizer recognizer,
        ISpeechSynthesizer synthesizer,
        ISkillDispatcher dispatcher,
        ISettingsService settings,
        ILocalizer localizer,
        IShellController shell,
        GreetingComposer greetingComposer,
        IClock clock,
        ILogger<AssistantEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(greetingComposer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _recognizer = recognizer;
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
    public AssistantState State => (AssistantState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is not null)
        {
            return;
        }

        _recognizer.Recognized += OnSpeechRecognized;
        _settings.Changed += OnSettingsChanged;

        _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);

        LauraSettings settings = _settings.Current;
        _localizer.SetCulture(settings.ResolveCulture());

        // The greeting is intentionally queued before settings: the first
        // thing Laura says must be a greeting, never a technical warning.
        if (settings.GreetOnStartup)
        {
            Enqueue(new SpeechRequested(_greetingComposer.ComposeStartupBriefing()));
        }

        await EnqueueAndWaitAsync(
            completion => new SettingsApplied(settings) { Completion = completion },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Assistant engine started.");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is null)
        {
            return;
        }

        _recognizer.Recognized -= OnSpeechRecognized;
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

        if (_recognizerRunning)
        {
            await _recognizer.StopAsync(CancellationToken.None).ConfigureAwait(false);
            _recognizerRunning = false;
        }

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
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetime.Dispose();
    }

    // === Processing Loop ===

    /// <summary>
    /// Consumes the message queue until the engine is stopped.
    ///
    /// Args:
    ///     cancellationToken: Token that ends the loop.
    ///
    /// Returns:
    ///     A task completed when the loop ends.
    /// </summary>
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
            // Encerramento normal.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The assistant processing loop ended with a failure.");
        }
    }

    /// <summary>
    /// Handles a queue message, ensuring anyone waiting for it is released.
    ///
    /// Args:
    ///     message: Message to handle.
    ///     cancellationToken: Token que aborta o tratamento.
    ///
    /// Returns:
    ///     A task completed when the message has been handled.
    /// </summary>
    private async Task HandleAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message)
            {
                case SpeechHeard heard:
                    await HandleSpeechHeardAsync(heard.Result, cancellationToken).ConfigureAwait(false);
                    break;

                case CommandSubmitted submitted:
                    await ExecuteCommandAsync(
                        submitted.Text,
                        confidence: 1.0,
                        SkillRequestSource.Text,
                        cancellationToken).ConfigureAwait(false);
                    break;

                case SpeechRequested requested:
                    await SpeakInternalAsync(requested.Text, cancellationToken).ConfigureAwait(false);
                    break;

                case ListeningExpired expired:
                    await HandleListeningExpiredAsync(expired.Epoch, cancellationToken).ConfigureAwait(false);
                    break;

                case SettingsApplied applied:
                    await ApplySettingsAsync(applied.Settings, cancellationToken).ConfigureAwait(false);
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
            _logger.LogError(exception, "Falha ao tratar {Message}.", message.GetType().Name);
        }
        finally
        {
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Decides what to do with a transcription received from the microphone.
    ///
    /// Args:
    ///     result: Transcription and its confidence.
    ///     cancellationToken: Token que aborta o tratamento.
    ///
    /// Returns:
    ///     A task completed when the transcription has been processed.
    /// </summary>
    private async Task HandleSpeechHeardAsync(RecognitionResult result, CancellationToken cancellationToken)
    {
        RecognitionOptions options = _settings.Current.Recognition;

        if (result.Confidence < options.MinimumConfidence)
        {
            _logger.LogDebug(
                "Transcription discarded for low confidence ({Confidence:P0}): \"{Text}\".",
                result.Confidence,
                result.Text);

            return;
        }

        string text = TextNormalizer.Normalize(result.Text);

        if (text.Length == 0)
        {
            return;
        }

        bool wakeDetected = WakeWordDetector.TryDetect(text, options, out string remainder);

        if (State is AssistantState.ListeningForCommand)
        {
            // Repeating the trigger while listening only renews the waiting window.
            string command = wakeDetected ? remainder : text;

            if (command.Length == 0)
            {
                await BeginListeningWindowAsync(speakAcknowledgement: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ExecuteCommandAsync(command, result.Confidence, SkillRequestSource.Voice, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        if (!wakeDetected)
        {
            return;
        }

        if (options.AllowInlineCommand && remainder.Length > 0)
        {
            await ExecuteCommandAsync(remainder, result.Confidence, SkillRequestSource.Voice, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        await BeginListeningWindowAsync(speakAcknowledgement: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the window where Laura waits for the command after waking.
    ///
    /// Args:
    ///     speakAcknowledgement: <see langword="true"/> para responder "Sim?" antes de escutar.
    ///     cancellationToken: Token that aborts the operation.
    ///
    /// Returns:
    ///     A task completed when command mode is active.
    /// </summary>
    private async Task BeginListeningWindowAsync(bool speakAcknowledgement, CancellationToken cancellationToken)
    {
        if (speakAcknowledgement)
        {
            await SpeakInternalAsync(
                _localizer.Get(LocalizationKeys.Assistant.Acknowledge),
                cancellationToken).ConfigureAwait(false);
        }

        long epoch = Interlocked.Increment(ref _listeningEpoch);

        SetState(AssistantState.ListeningForCommand);
        await SetRecognizerModeAsync(RecognitionMode.Command, cancellationToken).ConfigureAwait(false);

        ScheduleListeningTimeout(epoch, _settings.Current.Recognition.CommandTimeout);
    }

    /// <summary>
    /// Closes the listening window when the user did not say anything in time.
    ///
    /// Args:
    ///     epoch: Identifier of the expired window.
    ///     cancellationToken: Token that aborts the operation.
    ///
    /// Returns:
    ///     A task completed when the state returned to waiting for the trigger.
    /// </summary>
    private async Task HandleListeningExpiredAsync(long epoch, CancellationToken cancellationToken)
    {
        // A newer window already replaced this one; the notice arrived late.
        if (epoch != Interlocked.Read(ref _listeningEpoch))
        {
            return;
        }

        _logger.LogDebug("The listening window expired with no command.");
        _commandRetries = 0;

        // The user woke Laura and then stayed quiet: acknowledge it out loud so the
        // silence does not feel like she simply stopped working.
        await SpeakInternalAsync(
            _localizer.Get(LocalizationKeys.Assistant.HeardNothing),
            cancellationToken).ConfigureAwait(false);

        await ReturnToIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches a command and speaks the response.
    ///
    /// Args:
    ///     text: Command text, already without the wake word.
    ///     confidence: Recognizer confidence, from 0.0 to 1.0.
    ///     source: Command source.
    ///     cancellationToken: Token that aborts execution.
    ///
    /// Returns:
    ///     A task completed when the response has finished being spoken.
    /// </summary>
    private async Task ExecuteCommandAsync(
        string text,
        double confidence,
        SkillRequestSource source,
        CancellationToken cancellationToken)
    {
        SetState(AssistantState.Working);

        SkillRequest request = SkillRequest.Create(text, _localizer.Culture, _clock.Now, source, confidence);
        SkillResponse response = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.Handled && source is SkillRequestSource.Voice)
        {
            await HandleUnrecognizedCommandAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _commandRetries = 0;
        string? spokenText = response.Handled
            ? response.SpokenText
            : _localizer.Get(LocalizationKeys.Assistant.NotUnderstood);

        await ReturnToIdleAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(spokenText))
        {
            await SpeakInternalAsync(spokenText, cancellationToken).ConfigureAwait(false);
        }

        if (response.RequestsShutdown)
        {
            _shell.Shutdown();
        }
    }

    /// <summary>
    /// Responds to a voice command that no skill understood.
    ///
    /// Instead of falling silent, Laura says she did not catch it and keeps the
    /// command window open so the user can simply try again — up to a small limit,
    /// after which she steps back to waiting for the wake word.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts the operation.
    ///
    /// Returns:
    ///     A task that completes once Laura has spoken and resumed listening.
    /// </summary>
    private async Task HandleUnrecognizedCommandAsync(CancellationToken cancellationToken)
    {
        await SpeakInternalAsync(
            _localizer.Get(LocalizationKeys.Assistant.NotUnderstood),
            cancellationToken).ConfigureAwait(false);

        if (_commandRetries >= MaxCommandRetries)
        {
            _commandRetries = 0;
            await ReturnToIdleAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _commandRetries++;
        await BeginListeningWindowAsync(speakAcknowledgement: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fala um texto, suspendendo a escuta enquanto isso.
    ///
    /// Without this suspension the recognizer would transcribe Laura's own voice and she
    /// responderia a si mesma.
    ///
    /// Args:
    ///     text: Texto a falar.
    ///     cancellationToken: Token que interrompe a fala.
    ///
    /// Returns:
    ///     A task completed when speech ends and listening is restored.
    /// </summary>
    private async Task SpeakInternalAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AssistantState previousState = State;
        RecognitionMode previousMode = _recognizer.Mode;
        bool shouldMute = _recognizerRunning && previousMode is not RecognitionMode.Idle;

        SetState(AssistantState.Speaking);

        try
        {
            if (shouldMute)
            {
                await SetRecognizerModeAsync(RecognitionMode.Idle, cancellationToken).ConfigureAwait(false);
            }

            LauraSettings settings = _settings.Current;
            var request = new SpeechRequest(text, settings.Voice, settings.Culture);

            await _synthesizer.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldMute)
            {
                await SetRecognizerModeAsync(previousMode, CancellationToken.None).ConfigureAwait(false);
            }

            SetState(previousState);
        }
    }

    // === State and listening ===

    /// <summary>
    /// Applies the settings to the recognizer and the active language.
    ///
    /// Args:
    ///     settings: Settings to apply.
    ///     cancellationToken: Token that aborts the operation.
    ///
    /// Returns:
    ///     A task that completes when the settings are in effect.
    /// </summary>
    private async Task ApplySettingsAsync(LauraSettings settings, CancellationToken cancellationToken)
    {
        _localizer.SetCulture(settings.ResolveCulture());

        // The known command phrases feed a constrained grammar, so recognition of
        // built-in commands is far more accurate than free dictation.
        RecognitionOptions options = settings.Recognition with { CommandPhrases = GatherCommandPhrases() };

        if (!options.Enabled)
        {
            if (_recognizerRunning)
            {
                await _recognizer.StopAsync(cancellationToken).ConfigureAwait(false);
                _recognizerRunning = false;
            }

            SetState(AssistantState.ListeningDisabled);
            return;
        }

        if (_recognizerRunning)
        {
            await _recognizer.ApplyOptionsAsync(options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _recognizer.StartAsync(options, cancellationToken).ConfigureAwait(false);
            _recognizerRunning = _recognizer.IsAvailable;

            if (!_recognizerRunning)
            {
                // A microphone failure is surfaced in the settings window, not
                // spoken: a warning on every startup would be annoying and not
                // actionable.
                SetState(AssistantState.RecognitionUnavailable);
                _logger.LogWarning("Speech recognition unavailable; Laura responds through the window only.");

                return;
            }
        }

        await ReturnToIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Collects the fixed command phrases from the active language file.
    ///
    /// These are the phrases whose responses are fixed (time, date, weather, and so
    /// on); constraining the grammar to them sharply improves accuracy. Free-form
    /// commands still fall through to the dictation fallback in the recognizer.
    ///
    /// Returns:
    ///     The distinct command phrases for the active language.
    /// </summary>
    private IReadOnlyList<string> GatherCommandPhrases()
    {
        string[] keys =
        [
            LocalizationKeys.Skills.TimePhrases,
            LocalizationKeys.Skills.DatePhrases,
            LocalizationKeys.Skills.WeatherPhrases,
            LocalizationKeys.Skills.GreetingPhrases,
            LocalizationKeys.Skills.HelpPhrases,
            LocalizationKeys.Skills.FarewellPhrases,
            LocalizationKeys.Skills.LockPhrases,
            LocalizationKeys.Skills.VolumeUpPhrases,
            LocalizationKeys.Skills.VolumeDownPhrases,
            LocalizationKeys.Skills.VolumeMutePhrases,
            LocalizationKeys.Skills.SettingsPhrases,
        ];

        return [.. keys
            .SelectMany(_localizer.GetPhrases)
            .Where(static phrase => !string.IsNullOrWhiteSpace(phrase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Returns to listening only for the wake word, invalidating any open window.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts the operation.
    ///
    /// Returns:
    ///     A task completed when idle state has been restored.
    /// </summary>
    private async Task ReturnToIdleAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listeningEpoch);

        if (!_settings.Current.Recognition.Enabled)
        {
            SetState(AssistantState.ListeningDisabled);
            return;
        }

        if (!_recognizerRunning)
        {
            SetState(AssistantState.RecognitionUnavailable);
            return;
        }

        await SetRecognizerModeAsync(RecognitionMode.WakeWord, cancellationToken).ConfigureAwait(false);
        SetState(AssistantState.AwaitingWakeWord);
    }

    /// <summary>
    /// Changes the recognizer mode when it is active.
    ///
    /// Args:
    ///     mode: Modo desejado.
    ///     cancellationToken: Token que aborta a troca.
    ///
    /// Returns:
    ///     A task completed when the mode has been applied, or immediately
    ///     when there is no active recognizer.
    /// </summary>
    private Task SetRecognizerModeAsync(RecognitionMode mode, CancellationToken cancellationToken) =>
        _recognizerRunning
            ? _recognizer.SetModeAsync(mode, cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Schedules the end of the listening window.
    ///
    /// O aviso volta pela fila em vez de agir direto, para que o estado continue
    /// being changed by a single thread.
    ///
    /// Args:
    ///     epoch: Identifier of the scheduled window.
    ///     timeout: Window duration.
    /// </summary>
    private void ScheduleListeningTimeout(long epoch, TimeSpan timeout)
    {
        _ = Task.Delay(timeout, _lifetime.Token).ContinueWith(
            _ => _messages.Writer.TryWrite(new ListeningExpired(epoch)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Publishes a new state, ignoring repeats.
    ///
    /// Args:
    ///     state: Estado a publicar.
    /// </summary>
    private void SetState(AssistantState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) == (int)state)
        {
            return;
        }

        StateChanged?.Invoke(this, state);
    }

    // === Entrada de mensagens ===

    /// <summary>
    /// Enqueues a message, releasing anyone waiting if the queue is already closed.
    ///
    /// Args:
    ///     message: Message to enqueue.
    /// </summary>
    private void Enqueue(EngineMessage message)
    {
        if (!_messages.Writer.TryWrite(message))
        {
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Enqueues a message and waits for the loop to process it.
    ///
    /// Args:
    ///     factory: Creates the message already associated with the completion signal.
    ///     cancellationToken: Token que aborta a espera.
    ///
    /// Returns:
    ///     A task completed when the message has been handled.
    /// </summary>
    private Task EnqueueAndWaitAsync(
        Func<TaskCompletionSource, EngineMessage> factory,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(factory(completion));
        return completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Forwards a recognizer transcription to the queue.
    ///
    /// Returns immediately: blocking here would hold the speech engine audio thread.
    ///
    /// Args:
    ///     sender: Reconhecedor que emitiu o evento.
    ///     result: Received transcription.
    /// </summary>
    private void OnSpeechRecognized(object? sender, RecognitionResult result) =>
        Enqueue(new SpeechHeard(result));

    /// <summary>
    /// Forwards a settings change to the queue.
    ///
    /// Args:
    ///     sender: Settings service.
    ///     settings: Already-current settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, LauraSettings settings) =>
        Enqueue(new SettingsApplied(settings));

    // === Mensagens internas ===

    /// <summary>
    /// Item da fila de processamento do motor.
    /// </summary>
    private abstract record EngineMessage
    {
        /// <summary>
        /// Gets the signal completed after handling, when someone is waiting for
        /// the message. Fire-and-forget messages leave it null.
        /// </summary>
        public TaskCompletionSource? Completion { get; init; }
    }

    /// <summary>Transcription from the microphone.</summary>
    private sealed record SpeechHeard(RecognitionResult Result) : EngineMessage;

    /// <summary>Comando enviado em texto pela interface.</summary>
    private sealed record CommandSubmitted(string Text) : EngineMessage;

    /// <summary>Pedido de fala avulso.</summary>
    private sealed record SpeechRequested(string Text) : EngineMessage;

    /// <summary>Notice that the listening window ended.</summary>
    private sealed record ListeningExpired(long Epoch) : EngineMessage;

    /// <summary>Settings to apply to the recognizer and language.</summary>
    private sealed record SettingsApplied(LauraSettings Settings) : EngineMessage;
}

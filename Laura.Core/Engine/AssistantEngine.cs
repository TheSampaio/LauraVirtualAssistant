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
/// Orquestra o ciclo completo da assistente: escutar, entender, agir e responder.
///
/// Todo o trabalho acontece em um único laço de consumo alimentado por uma fila.
/// Escolha deliberada: os eventos do reconhecedor chegam em threads do motor de
/// áudio e a interface chama o motor pela thread de UI — enfileirar em vez de
/// executar no chamador mantém as duas livres enquanto Laura ouve ou fala, e
/// dispensa travas para proteger o estado, já que só o laço o modifica.
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
    /// Inicializa o motor com suas dependências.
    ///
    /// Args:
    ///     recognizer: Motor de reconhecimento de fala.
    ///     synthesizer: Motor de síntese de voz.
    ///     dispatcher: Despachante de habilidades.
    ///     settings: Configurações vigentes e suas notificações de mudança.
    ///     localizer: Fonte dos textos no idioma ativo.
    ///     shell: Controle da camada visual, usado para encerrar a aplicação.
    ///     greetingComposer: Compositor da saudação de abertura.
    ///     clock: Fonte de data e hora.
    ///     logger: Destino dos registros de diagnóstico.
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

        // A saudação é enfileirada antes das configurações de propósito: a primeira
        // coisa que Laura diz tem de ser um cumprimento, nunca um aviso técnico.
        if (settings.GreetOnStartup)
        {
            Enqueue(new SpeechRequested(_greetingComposer.ComposeStartupBriefing()));
        }

        await EnqueueAndWaitAsync(
            completion => new SettingsApplied(settings) { Completion = completion },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Motor da assistente iniciado.");
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
            // O encerramento não deve travar por causa do laço de processamento.
        }

        _worker = null;

        if (_recognizerRunning)
        {
            await _recognizer.StopAsync(CancellationToken.None).ConfigureAwait(false);
            _recognizerRunning = false;
        }

        SetState(AssistantState.Stopped);
        _logger.LogInformation("Motor da assistente parado.");
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

    // === Laço de processamento ===

    /// <summary>
    /// Consome a fila de mensagens até o motor ser parado.
    ///
    /// Args:
    ///     cancellationToken: Token que encerra o laço.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o laço termina.
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
            _logger.LogError(exception, "O laço de processamento da assistente terminou com falha.");
        }
    }

    /// <summary>
    /// Trata uma mensagem da fila, garantindo que quem espera por ela seja liberado.
    ///
    /// Args:
    ///     message: Mensagem a tratar.
    ///     cancellationToken: Token que aborta o tratamento.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a mensagem foi tratada.
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
                    _logger.LogWarning("Mensagem desconhecida na fila: {Message}.", message.GetType().Name);
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
    /// Decide o que fazer com uma transcrição recebida do microfone.
    ///
    /// Args:
    ///     result: Transcrição e sua confiança.
    ///     cancellationToken: Token que aborta o tratamento.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a transcrição foi processada.
    /// </summary>
    private async Task HandleSpeechHeardAsync(RecognitionResult result, CancellationToken cancellationToken)
    {
        RecognitionOptions options = _settings.Current.Recognition;

        if (result.Confidence < options.MinimumConfidence)
        {
            _logger.LogDebug(
                "Transcrição descartada por confiança baixa ({Confidence:P0}): \"{Text}\".",
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
            // Repetir o gatilho durante a escuta apenas renova a janela de espera.
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
    /// Abre a janela em que Laura espera o comando após ser despertada.
    ///
    /// Args:
    ///     speakAcknowledgement: <see langword="true"/> para responder "Sim?" antes de escutar.
    ///     cancellationToken: Token que aborta a operação.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o modo de comando está ativo.
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
    /// Fecha a janela de escuta quando o usuário não disse nada a tempo.
    ///
    /// Args:
    ///     epoch: Identificador da janela que expirou.
    ///     cancellationToken: Token que aborta a operação.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o estado voltou à espera do gatilho.
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
    /// Despacha um comando e fala a resposta.
    ///
    /// Args:
    ///     text: Texto do comando, já sem a palavra de ativação.
    ///     confidence: Confiança do reconhecedor, de 0.0 a 1.0.
    ///     source: Origem do comando.
    ///     cancellationToken: Token que aborta a execução.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a resposta terminou de ser falada.
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
    /// Sem essa suspensão o reconhecedor transcreveria a própria voz de Laura e ela
    /// responderia a si mesma.
    ///
    /// Args:
    ///     text: Texto a falar.
    ///     cancellationToken: Token que interrompe a fala.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a fala termina e a escuta é restaurada.
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
    /// Volta a escutar apenas a palavra de ativação, invalidando qualquer janela aberta.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a operação.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o estado de repouso foi restaurado.
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
    /// Troca o modo do reconhecedor quando ele está ativo.
    ///
    /// Args:
    ///     mode: Modo desejado.
    ///     cancellationToken: Token que aborta a troca.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o modo foi aplicado, ou imediatamente
    ///     quando não há reconhecedor ativo.
    /// </summary>
    private Task SetRecognizerModeAsync(RecognitionMode mode, CancellationToken cancellationToken) =>
        _recognizerRunning
            ? _recognizer.SetModeAsync(mode, cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Agenda o fim da janela de escuta.
    ///
    /// O aviso volta pela fila em vez de agir direto, para que o estado continue
    /// sendo alterado por uma única thread.
    ///
    /// Args:
    ///     epoch: Identificador da janela agendada.
    ///     timeout: Duração da janela.
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
    /// Publica um novo estado, ignorando repetições.
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
    /// Enfileira uma mensagem, liberando quem a aguarda caso a fila já esteja fechada.
    ///
    /// Args:
    ///     message: Mensagem a enfileirar.
    /// </summary>
    private void Enqueue(EngineMessage message)
    {
        if (!_messages.Writer.TryWrite(message))
        {
            message.Completion?.TrySetResult();
        }
    }

    /// <summary>
    /// Enfileira uma mensagem e aguarda o laço processá-la.
    ///
    /// Args:
    ///     factory: Cria a mensagem já associada ao sinalizador de conclusão.
    ///     cancellationToken: Token que aborta a espera.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a mensagem foi tratada.
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
    /// Repassa uma transcrição do reconhecedor para a fila.
    ///
    /// Retorna de imediato: bloquear aqui seguraria a thread de áudio do motor de fala.
    ///
    /// Args:
    ///     sender: Reconhecedor que emitiu o evento.
    ///     result: Transcrição recebida.
    /// </summary>
    private void OnSpeechRecognized(object? sender, RecognitionResult result) =>
        Enqueue(new SpeechHeard(result));

    /// <summary>
    /// Repassa uma mudança de configuração para a fila.
    ///
    /// Args:
    ///     sender: Serviço de configurações.
    ///     settings: Configurações já vigentes.
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
        /// Obtém o sinalizador concluído após o tratamento, quando há quem aguarde
        /// a mensagem. Mensagens disparadas e esquecidas o deixam nulo.
        /// </summary>
        public TaskCompletionSource? Completion { get; init; }
    }

    /// <summary>Transcrição vinda do microfone.</summary>
    private sealed record SpeechHeard(RecognitionResult Result) : EngineMessage;

    /// <summary>Comando enviado em texto pela interface.</summary>
    private sealed record CommandSubmitted(string Text) : EngineMessage;

    /// <summary>Pedido de fala avulso.</summary>
    private sealed record SpeechRequested(string Text) : EngineMessage;

    /// <summary>Aviso de que a janela de escuta terminou.</summary>
    private sealed record ListeningExpired(long Epoch) : EngineMessage;

    /// <summary>Configurações a aplicar ao reconhecedor e ao idioma.</summary>
    private sealed record SettingsApplied(LauraSettings Settings) : EngineMessage;
}

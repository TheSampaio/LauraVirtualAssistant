using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Laura.Platform.Windows.Speech;

/// <summary>Routes explicit microphone capture to Gemini Live or local Whisper while preserving one HUD activity surface.</summary>
public sealed class LocalVoiceSession : IAsyncDisposable
{
    private readonly ISettingsService settings;
    private readonly IVoiceInput input;
    private readonly KokoroSpeechSynthesizer speech;
    private readonly ISpeechSynthesizer routedSpeech;
    private readonly GeminiLiveClient gemini;
    private readonly IAssistantEngine engine;
    private readonly ListeningControl listening;
    private readonly ILogger<LocalVoiceSession> logger;
    /// <summary>Observes immediate mute and local asset changes independently of any UI save workflow.</summary>
    public LocalVoiceSession(ISettingsService settings, IVoiceInput input, KokoroSpeechSynthesizer speech,
        IAssistantEngine engine, ListeningControl listening, ILogger<LocalVoiceSession> logger,
        ISpeechSynthesizer routedSpeech, GeminiLiveClient gemini)
    {
        this.settings = settings; this.input = input; this.speech = speech; this.engine = engine; this.listening = listening; this.logger = logger;
        this.routedSpeech = routedSpeech; this.gemini = gemini;
        gemini.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
        gemini.StandbyRequested += (_, args) => StandbyRequested?.Invoke(this, args);
        gemini.Interrupted += (_, args) => Interrupted?.Invoke(this, args);
        gemini.MessageReceived += (_, message) => MessageReceived?.Invoke(this, message);
        settings.Changed += OnSettingsChanged;
    }
    /// <summary>Closes active capture immediately when local input becomes unavailable or the owner mutes it.</summary>
    private void OnSettingsChanged(object? sender, LauraSettings value)
    { if (value.MicrophoneMuted || !IsAvailable) { Interrupt(); } }
    private readonly SemaphoreSlim _transitions = new(1, 1);
    private CancellationTokenSource? _session;
    private Task? _worker;
    private TaskCompletionSource<bool>? _finish;
    private WaveInEvent? _recording;
    private int _starting, _thinking;
    private float _inputLevel;
    /// <summary>Publishes content-free native activity for the HUD.</summary>
    public event EventHandler<string>? StatusChanged;
    /// <summary>Signals a user-requested return to standby.</summary>
    public event EventHandler? StandbyRequested;
    /// <summary>Signals explicit interruption and fences provisional presentation.</summary>
    public event EventHandler? Interrupted;
    /// <summary>Publishes completed native Gemini voice turns for the chat surface.</summary>
    public event EventHandler<ConversationMessage>? MessageReceived;
    /// <summary>Gets readiness of the selected microphone route.</summary>
    public bool IsAvailable => UseGemini ? gemini.IsAvailable : input.IsAvailable;
    private bool UseGemini => settings.Current.ProviderSelection == ConversationProviderSelection.Gemini
        || settings.Current.ProviderSelection == ConversationProviderSelection.Automatic
            && settings.Current.GenerativeAi.LightProvider == LightProviderSelection.Gemini;
    /// <summary>Gets whether a microphone is physically open.</summary>
    public bool IsListening => gemini.IsListening || Volatile.Read(ref _recording) is not null;
    /// <summary>Gets whether an explicit capture operation is starting.</summary>
    public bool IsCaptureStarting => gemini.IsCaptureStarting || Volatile.Read(ref _starting) != 0;
    /// <summary>Gets whether local recognition or the shared text engine is processing a turn.</summary>
    public bool IsThinking => gemini.IsThinking || Volatile.Read(ref _thinking) != 0;
    /// <summary>Gets provider-independent speech activity.</summary>
    public bool IsSpeaking => routedSpeech.IsSpeaking;
    /// <summary>Gets actual local PCM playback activity.</summary>
    public bool IsPlaying => gemini.IsPlaying || speech.IsPlaying;
    /// <summary>Gets current microphone RMS amplitude.</summary>
    public float InputLevel => gemini.IsListening ? gemini.InputLevel : Volatile.Read(ref _inputLevel);
    /// <summary>Gets current Kokoro PCM playback amplitude.</summary>
    public float OutputLevel => Math.Max(gemini.OutputLevel, speech.OutputLevel);
    /// <summary>Reflects shared engine inference activity without creating a provider session.</summary>
    public void SetThinking() { Volatile.Write(ref _thinking, 1); StatusChanged?.Invoke(this, "Thinking"); }
    /// <summary>Returns the HUD to ready after a completed or canceled operation.</summary>
    public void ReturnToReady() { Volatile.Write(ref _thinking, 0); StatusChanged?.Invoke(this, "Ready"); }

    /// <summary>Starts only an explicit unmuted local capture. Args: pushToTalk: Finish on chord release; cancellationToken: Start/session lifetime.</summary>
    public async Task StartListeningAsync(bool pushToTalk = false, CancellationToken cancellationToken = default)
    {
        if (UseGemini) { await gemini.StartListeningAsync(pushToTalk, cancellationToken).ConfigureAwait(false); return; }
        if (Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("UI diagnostics cannot open a microphone."); }
        if (!IsAvailable || settings.Current.MicrophoneMuted) { throw new InvalidOperationException("Local recognition is unavailable or muted."); }
        await _transitions.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null) { return; }
            Volatile.Write(ref _starting, 1);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _session = lifetime;
            _worker = Task.Run(() => RunAsync(lifetime, pushToTalk, ready), CancellationToken.None);
            await ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _starting, 0); _transitions.Release(); }
    }

    /// <summary>Captures bounded local audio, closes the device before inference and suspends capture throughout playback.</summary>
    private async Task RunAsync(CancellationTokenSource lifetime, bool pushToTalk, TaskCompletionSource ready)
    {
        try
        {
            do
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (settings.Current.MicrophoneMuted) { break; }
                using var bytes = new MemoryStream();
                var endpoint = new SpeechEndpointDetector(settings.Current.GenerativeAi.SilenceDurationMs);
                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _finish = finished;
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (var capture = new WaveInEvent { DeviceNumber = settings.Current.InputDevice < 0 ? 0 : settings.Current.InputDevice,
                    WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 40, NumberOfBuffers = 3 })
                {
                    capture.DataAvailable += (_, args) =>
                    {
                        if (lifetime.IsCancellationRequested || finished.Task.IsCompleted) { return; }
                        if (routedSpeech.IsSpeaking)
                        {
                            // A typed reply or native announcement can start while this explicit microphone session is open.
                            // Discard its speaker echo and any incomplete utterance instead of feeding it back to Whisper.
                            bytes.SetLength(0); endpoint = new SpeechEndpointDetector(settings.Current.GenerativeAi.SilenceDurationMs);
                            Volatile.Write(ref _inputLevel, 0); return;
                        }
                        float level = AudioLevel.Measure(args.Buffer.AsSpan(0, args.BytesRecorded)); Volatile.Write(ref _inputLevel, level);
                        if (bytes.Length + args.BytesRecorded <= 960000) { bytes.Write(args.Buffer, 0, args.BytesRecorded); }
                        if (endpoint.Observe(level, args.BytesRecorded * 1000 / 32000) && !pushToTalk || bytes.Length >= 960000)
                        { finished.TrySetResult(true); }
                    };
                    capture.RecordingStopped += (_, args) => { if (args.Exception is { } error) { stopped.TrySetException(error); } else { stopped.TrySetResult(); } };
                    _recording = capture;
                    try
                    {
                        capture.StartRecording(); ready.TrySetResult(); StatusChanged?.Invoke(this, "Listening");
                        await finished.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        capture.StopRecording();
                        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
                        _recording = null; Volatile.Write(ref _inputLevel, 0);
                    }
                }
                if (!endpoint.HasSpeech || !await finished.Task.ConfigureAwait(false)) { break; }
                SetThinking();
                string transcript = await input.TranscribeAsync(bytes.ToArray(), settings.Current.Culture, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                if (listening.IsRestRequest(transcript))
                {
                    await engine.SpeakAsync(listening.RestConfirmation, lifetime.Token).ConfigureAwait(false);
                    StandbyRequested?.Invoke(this, EventArgs.Empty); break;
                }
                if (transcript.Length > 0) { await engine.SubmitVoiceCommandAsync(transcript, lifetime.Token).ConfigureAwait(false); }
            }
            while (!pushToTalk && settings.Current.GenerativeAi.ContinuousListening && !settings.Current.MicrophoneMuted);
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(); }
        catch (Exception error) when (error is InvalidOperationException or IOException or NAudio.MmException or TimeoutException)
        { ready.TrySetException(new InvalidOperationException("Local microphone or recognition failed.")); logger.LogWarning("Local voice input failed ({ErrorType}).", error.GetType().Name); }
        finally
        {
            _recording = null; _finish = null; Volatile.Write(ref _inputLevel, 0);
            if (ReferenceEquals(_session, lifetime)) { _session = null; }
            lifetime.Dispose(); ReturnToReady();
        }
    }
    /// <summary>Closes local capture immediately. Args: submitCaptured: Explicit held-chord release may submit its valid captured utterance.</summary>
    public async Task StopListeningAsync(bool submitCaptured = false)
    {
        await gemini.StopListeningAsync().ConfigureAwait(false);
        if (submitCaptured && _finish is { } finish) { finish.TrySetResult(true); }
        else { try { _session?.Cancel(); } catch (ObjectDisposedException) { } }
        if (_worker is { } worker) { await worker.ConfigureAwait(false); }
    }
    /// <summary>Interrupts only the active voice workflow and provider-independent playback.</summary>
    public void Interrupt()
    {
        try { _session?.Cancel(); } catch (ObjectDisposedException) { }
        gemini.Interrupt(); routedSpeech.CancelSpeech(); Interrupted?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Returns to standby without asking any model or transmitting audio.</summary>
    public Task RestAsync(bool sayGoodbye = false)
    { StandbyRequested?.Invoke(this, EventArgs.Empty); Interrupt(); return Task.CompletedTask; }
    /// <summary>Stops local capture before settings changes or shutdown.</summary>
    public Task ReconfigureAsync() => StopListeningAsync();
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { settings.Changed -= OnSettingsChanged; Interrupt(); await StopListeningAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
}

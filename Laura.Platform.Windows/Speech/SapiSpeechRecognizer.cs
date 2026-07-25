using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;
using Laura.Platform.Windows.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

// SAPI also defines RecognitionResult; the alias makes clear which of the two types
// each piece manipulates.
using CoreRecognitionResult = Laura.Core.Speech.RecognitionResult;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Speech recognizer backed by SAPI.
///
/// It keeps a wake grammar and a command grammar loaded and only toggles which is
/// enabled — reloading grammars would require stopping and restarting recognition on
/// every activation, deaf for a moment right when the user starts speaking. The wake
/// grammar also carries the command as an optional tail, so "Hey Laura, what time is
/// it" is understood in one breath instead of only hearing the wake word.
/// </summary>
public sealed class SapiSpeechRecognizer : ISpeechRecognizer
{
    // 16 kHz mono 16-bit PCM: what a chosen MME capture delivers and what SAPI wants.
    private static readonly SpeechAudioFormatInfo StreamFormat =
        new(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<SapiSpeechRecognizer> _logger;

    private SpeechRecognitionEngine? _engine;
    private WaveInEvent? _capture;
    private SpeechStreamer? _captureStream;
    private Grammar? _wakeGrammar;
    private Grammar? _commandGrammar;
    private RecognitionOptions _options = RecognitionOptions.Default;
    private string? _engineCulture;
    private int _mode = (int)RecognitionMode.Idle;
    private bool _disposed;

    /// <summary>
    /// Initializes the recognizer without touching the audio device yet.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public SapiSpeechRecognizer(ILogger<SapiSpeechRecognizer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<CoreRecognitionResult>? Recognized;

    /// <inheritdoc />
    public RecognitionMode Mode => (RecognitionMode)Volatile.Read(ref _mode);

    /// <inheritdoc />
    public bool IsAvailable => _engine is not null;

    /// <inheritdoc />
    public async Task StartAsync(RecognitionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _options = options;
            await Task.Run(() => Initialize(options), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.Run(Shutdown, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetModeAsync(RecognitionMode mode, CancellationToken cancellationToken = default)
    {
        if (_disposed || _engine is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ApplyMode(mode);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ApplyOptionsAsync(RecognitionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            bool cultureChanged = !string.Equals(_engineCulture, options.Culture, StringComparison.OrdinalIgnoreCase);
            bool deviceChanged = !string.Equals(
                _options.MicrophoneDeviceId ?? string.Empty,
                options.MicrophoneDeviceId ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            _options = options;

            await Task.Run(
                () =>
                {
                    // The culture and the input device are both fixed at engine
                    // construction, so changing either means rebuilding the engine.
                    if (cultureChanged || deviceChanged)
                    {
                        Shutdown();
                        Initialize(options);
                        return;
                    }

                    RebuildGrammars(options);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await Task.Run(Shutdown).ConfigureAwait(false);
        _gate.Dispose();
    }

    // === Engine lifecycle ===

    /// <summary>
    /// Creates the engine, binds the microphone and starts continuous recognition.
    ///
    /// Leaves the recognizer unavailable, instead of throwing, when there is no
    /// engine for the language or no microphone: Laura stays usable through the UI.
    ///
    /// Args:
    ///     options: Listening options to apply.
    /// </summary>
    private void Initialize(RecognitionOptions options)
    {
        RecognizerInfo? recognizerInfo = FindRecognizer(options.Culture);

        if (recognizerInfo is null)
        {
            _logger.LogWarning(
                "No recognition engine installed for {Culture}; voice listening will stay off.",
                options.Culture);

            return;
        }

        try
        {
            var engine = new SpeechRecognitionEngine(recognizerInfo);

            BindInput(engine, options);
            TuneSensitivity(engine);
            engine.SpeechRecognized += OnSpeechRecognized;

            _engine = engine;
            _engineCulture = options.Culture;

            RebuildGrammars(options);
            engine.RecognizeAsync(RecognizeMode.Multiple);

            _logger.LogInformation("Speech recognition active with {Recognizer}.", recognizerInfo.Name);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException or NAudio.MmException)
        {
            _logger.LogWarning(exception, "Could not start speech recognition.");
            Shutdown();
        }
    }

    /// <summary>
    /// Points the engine at the chosen microphone, or the default device.
    ///
    /// When a specific device is selected, its audio is captured with NAudio and
    /// piped into the engine, because System.Speech itself can only bind the default
    /// device.
    ///
    /// Args:
    ///     engine: Engine to bind.
    ///     options: Options holding the chosen device, if any.
    /// </summary>
    private void BindInput(SpeechRecognitionEngine engine, RecognitionOptions options)
    {
        int deviceNumber = WindowsAudioDeviceCatalog.ResolveDeviceNumber(options.MicrophoneDeviceId);

        if (deviceNumber < 0)
        {
            engine.SetInputToDefaultAudioDevice();
            return;
        }

        var stream = new SpeechStreamer();
        var capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(StreamFormat.SamplesPerSecond, StreamFormat.BitsPerSample, StreamFormat.ChannelCount),
            BufferMilliseconds = 50,
        };

        capture.DataAvailable += (_, args) => stream.Write(args.Buffer, 0, args.BytesRecorded);
        capture.StartRecording();

        _capture = capture;
        _captureStream = stream;
        engine.SetInputToAudioStream(stream, StreamFormat);
    }

    /// <summary>
    /// Relaxes the SAPI rejection threshold so quieter, less crisp speech is emitted.
    ///
    /// The application still applies its own confidence floor afterwards, so a low
    /// engine threshold improves pickup without letting noise through as commands.
    ///
    /// Args:
    ///     engine: Engine to tune.
    /// </summary>
    private static void TuneSensitivity(SpeechRecognitionEngine engine)
    {
        engine.MaxAlternates = 3;
        engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(600);
        engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(900);

        try
        {
            engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 35);
            engine.UpdateRecognizerSetting("ResponseSpeed", 150);
        }
        catch (KeyNotFoundException)
        {
            // Some engines do not expose these tuning knobs; the defaults are fine.
        }
    }

    /// <summary>
    /// Stops recognition and releases the microphone.
    /// </summary>
    private void Shutdown()
    {
        SpeechRecognitionEngine? engine = _engine;
        WaveInEvent? capture = _capture;
        SpeechStreamer? stream = _captureStream;

        _engine = null;
        _capture = null;
        _captureStream = null;
        _engineCulture = null;
        _wakeGrammar = null;
        _commandGrammar = null;
        Volatile.Write(ref _mode, (int)RecognitionMode.Idle);

        if (engine is not null)
        {
            engine.SpeechRecognized -= OnSpeechRecognized;

            try
            {
                engine.RecognizeAsyncCancel();
            }
            catch (InvalidOperationException)
            {
                // No recognition was in progress.
            }

            engine.Dispose();
        }

        if (capture is not null)
        {
            try
            {
                capture.StopRecording();
            }
            catch (NAudio.MmException)
            {
                // The device may already be gone; nothing more to do.
            }

            capture.Dispose();
        }

        stream?.Dispose();
    }

    // === Grammars ===

    /// <summary>
    /// Rebuilds and reloads the grammars from the current options.
    ///
    /// Args:
    ///     options: Options holding the wake and command phrases to recognize.
    /// </summary>
    private void RebuildGrammars(RecognitionOptions options)
    {
        SpeechRecognitionEngine? engine = _engine;

        if (engine is null)
        {
            return;
        }

        RecognitionMode currentMode = Mode;
        CultureInfo culture = CultureInfo.GetCultureInfo(options.Culture);

        engine.UnloadAllGrammars();

        var wakeChoices = new Choices([.. options.WakePhrases]);
        var wakeBuilder = new GrammarBuilder(wakeChoices) { Culture = culture };

        GrammarBuilder? command = BuildCommandBuilder(options, culture);

        // The command rides along as an optional tail of the wake grammar, so both
        // "Hey Laura" and "Hey Laura, what time is it" match the same rule.
        if (options.AllowInlineCommand && command is not null)
        {
            wakeBuilder.Append(command, 0, 1);
        }

        _wakeGrammar = LoadGrammar(engine, wakeBuilder, "wake");
        _commandGrammar = command is not null ? LoadGrammar(engine, command, "command") : null;

        ApplyMode(currentMode);
    }

    /// <summary>
    /// Builds the grammar fragment that recognizes a command.
    ///
    /// Known command phrases form a constrained choice — far more accurate than free
    /// dictation — while dictation is added as a fallback so arbitrary questions for
    /// the generative mode are still captured.
    ///
    /// Args:
    ///     options: Options holding the known command phrases.
    ///     culture: Culture the grammar is built for.
    ///
    /// Returns:
    ///     A command grammar fragment, or <see langword="null"/> when neither known
    ///     phrases nor dictation are available.
    /// </summary>
    private GrammarBuilder? BuildCommandBuilder(RecognitionOptions options, CultureInfo culture)
    {
        var alternatives = new List<GrammarBuilder>();

        if (options.CommandPhrases.Count > 0)
        {
            alternatives.Add(new GrammarBuilder(new Choices([.. options.CommandPhrases])));
        }

        if (TryBuildDictation(culture) is { } dictation)
        {
            alternatives.Add(dictation);
        }

        if (alternatives.Count == 0)
        {
            return null;
        }

        return new GrammarBuilder(new Choices([.. alternatives])) { Culture = culture };
    }

    /// <summary>
    /// Builds a free-dictation fragment when the installed language supports it.
    ///
    /// Args:
    ///     culture: Culture the fragment is built for.
    ///
    /// Returns:
    ///     A dictation fragment, or <see langword="null"/> when dictation is not
    ///     available for the language.
    /// </summary>
    private GrammarBuilder? TryBuildDictation(CultureInfo culture)
    {
        try
        {
            var builder = new GrammarBuilder { Culture = culture };
            builder.AppendDictation();
            return builder;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(exception, "Free dictation is not available for {Culture}.", culture.Name);
            return null;
        }
    }

    /// <summary>
    /// Compiles and loads a grammar, leaving it disabled.
    ///
    /// Args:
    ///     engine: Engine the grammar is loaded into.
    ///     builder: Grammar builder.
    ///     name: Grammar name, used in diagnostics.
    ///
    /// Returns:
    ///     The loaded grammar, or <see langword="null"/> when the engine rejected it.
    /// </summary>
    private Grammar? LoadGrammar(SpeechRecognitionEngine engine, GrammarBuilder builder, string name)
    {
        try
        {
            var grammar = new Grammar(builder) { Name = name, Enabled = false };
            engine.LoadGrammar(grammar);
            return grammar;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            _logger.LogWarning(exception, "The {Grammar} grammar could not be loaded.", name);
            return null;
        }
    }

    /// <summary>
    /// Enables only the grammar relevant to the requested mode.
    ///
    /// Args:
    ///     mode: Desired listening mode.
    /// </summary>
    private void ApplyMode(RecognitionMode mode)
    {
        SetGrammarEnabled(_wakeGrammar, mode is RecognitionMode.WakeWord);
        SetGrammarEnabled(_commandGrammar, mode is RecognitionMode.Command);

        Volatile.Write(ref _mode, (int)mode);
    }

    /// <summary>
    /// Enables or disables a grammar that may not have been loaded.
    ///
    /// Args:
    ///     grammar: Grammar to adjust, possibly null.
    ///     enabled: Desired state.
    /// </summary>
    private static void SetGrammarEnabled(Grammar? grammar, bool enabled)
    {
        if (grammar is not null)
        {
            grammar.Enabled = enabled;
        }
    }

    // === Recognition ===

    /// <summary>
    /// Converts the SAPI result and publishes it.
    ///
    /// Args:
    ///     sender: Engine that raised the event.
    ///     args: Recognition result.
    /// </summary>
    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs args)
    {
        if (args.Result is null)
        {
            return;
        }

        _logger.LogDebug(
            "Transcript \"{Text}\" with confidence {Confidence:P0}.",
            args.Result.Text,
            args.Result.Confidence);

        Recognized?.Invoke(this, new CoreRecognitionResult(args.Result.Text, args.Result.Confidence, Mode));
    }

    /// <summary>
    /// Picks the recognition engine best matching the requested culture.
    ///
    /// Args:
    ///     cultureName: Desired culture, as a BCP-47 tag.
    ///
    /// Returns:
    ///     The chosen engine, or <see langword="null"/> when none is installed.
    /// </summary>
    private RecognizerInfo? FindRecognizer(string cultureName)
    {
        IReadOnlyList<RecognizerInfo> installed = [.. SpeechRecognitionEngine.InstalledRecognizers()];

        if (installed.Count == 0)
        {
            return null;
        }

        RecognizerInfo? exact = installed.FirstOrDefault(
            info => string.Equals(info.Culture.Name, cultureName, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        string language = cultureName.Split('-')[0];

        RecognizerInfo? sameLanguage = installed.FirstOrDefault(
            info => info.Culture.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase));

        if (sameLanguage is not null)
        {
            _logger.LogWarning(
                "No engine for {Culture}; using {Fallback}. Install the language speech pack for better recognition.",
                cultureName,
                sameLanguage.Culture.Name);
        }

        return sameLanguage;
    }
}

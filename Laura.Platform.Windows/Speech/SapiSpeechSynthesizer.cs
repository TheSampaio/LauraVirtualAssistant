using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Speech.Synthesis;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Sintetizador de voz apoiado no SAPI, o mesmo motor que atende as vozes do Windows.
/// </summary>
public sealed class SapiSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly ILogger<SapiSpeechSynthesizer> _logger;

    private string? _selectedVoiceName;
    private bool _disposed;

    /// <summary>
    /// Initializes the synthesizer and routes output to the default device.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public SapiSpeechSynthesizer(ILogger<SapiSpeechSynthesizer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _synthesizer.SetOutputToDefaultAudioDevice();
    }

    /// <inheritdoc />
    public bool IsSpeaking => _synthesizer.State is SynthesizerState.Speaking;

    /// <inheritdoc />
    [SuppressMessage(
        "Globalization",
        "CA1304:Specify CultureInfo",
        Justification = "The interface needs to list every installed voice, not only voices for the active culture.")]
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices()
    {
        try
        {
            return [.. _synthesizer.GetInstalledVoices()
                .Where(static voice => voice.Enabled)
                .Select(static voice => new VoiceDescriptor(
                    voice.VoiceInfo.Name,
                    BuildDisplayName(voice.VoiceInfo),
                    voice.VoiceInfo.Culture.Name,
                    voice.VoiceInfo.Gender is VoiceGender.Female))];
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogError(exception, "Could not list installed voices.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return;
        }

        // Speech is serialized: two simultaneous utterances would collide in SAPI.
        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            CultureInfo culture = ResolveCulture(request.Culture);

            ApplyVoiceProfile(request.Voice, culture);
            await SpeakSsmlAsync(SsmlBuilder.Build(request.Text, request.Voice, culture), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _speechGate.Release();
        }
    }

    /// <inheritdoc />
    public void CancelSpeech()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
        catch (ObjectDisposedException)
        {
            // The synthesizer has already been disposed; there is no speech to stop.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        CancelSpeech();
        _synthesizer.Dispose();
        _speechGate.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Passes the SSML document to SAPI and waits for the utterance to finish.
    ///
    /// The SAPI API is event-based; the bridge to <c>async</c> is made with a
    /// <see cref="TaskCompletionSource"/> subscribed only during this utterance.
    ///
    /// Args:
    ///     ssml: Documento a falar.
    ///     cancellationToken: Token that interrupts the utterance.
    ///
    /// Returns:
    ///     A task completed when the utterance finishes or is interrupted.
    /// </summary>
    private async Task SpeakSsmlAsync(string ssml, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs args) => completion.TrySetResult();

        _synthesizer.SpeakCompleted += OnSpeakCompleted;

        await using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            CancelSpeech();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            _ = _synthesizer.SpeakSsmlAsync(ssml);
            await completion.Task.ConfigureAwait(false);
        }
        catch (FormatException exception)
        {
            // Text that produces invalid SSML cannot silence Laura completely.
            _logger.LogError(exception, "O SAPI recusou o documento SSML gerado.");
        }
        finally
        {
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
        }
    }

    /// <summary>
    /// Applies rate, volume, and voice choice before the utterance.
    ///
    /// Args:
    ///     profile: Perfil de voz configurado.
    ///     culture: Cultura usada para escolher uma voz quando nenhuma foi fixada.
    /// </summary>
    private void ApplyVoiceProfile(VoiceProfile profile, CultureInfo culture)
    {
        _synthesizer.Rate = profile.Rate;
        _synthesizer.Volume = profile.Volume;

        string? desiredVoice = profile.VoiceName ?? FindPreferredVoiceName(culture);

        if (desiredVoice is null || string.Equals(desiredVoice, _selectedVoiceName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _synthesizer.SelectVoice(desiredVoice);
            _selectedVoiceName = desiredVoice;
        }
        catch (ArgumentException exception)
        {
            // The saved voice may have been uninstalled; the system default voice is acceptable.
            _logger.LogWarning(exception, "Voice {Voice} is not available.", desiredVoice);
            _selectedVoiceName = null;
        }
    }

    /// <summary>
    /// Chooses the default voice for a culture.
    ///
    /// Laura is a feminine persona, so a feminine voice in the right language is
    /// preferred; only then does any voice for the language come next.
    ///
    /// Args:
    ///     culture: Cultura desejada.
    ///
    /// Returns:
    ///     O nome da voz escolhida, ou <see langword="null"/> quando nenhuma voz
    ///     matches the culture and the choice should stay with the system.
    /// </summary>
    private string? FindPreferredVoiceName(CultureInfo culture)
    {
        IReadOnlyList<VoiceDescriptor> voices = GetAvailableVoices();

        bool MatchesLanguage(VoiceDescriptor voice) => voice.Culture.StartsWith(
            culture.TwoLetterISOLanguageName,
            StringComparison.OrdinalIgnoreCase);

        VoiceDescriptor? preferred =
            voices.FirstOrDefault(voice => MatchesLanguage(voice) && voice.IsFemale)
            ?? voices.FirstOrDefault(MatchesLanguage);

        return preferred?.Name;
    }

    /// <summary>
    /// Resolves the received culture name, tolerating invalid values.
    ///
    /// Args:
    ///     cultureName: Nome da cultura no formato BCP-47.
    ///
    /// Returns:
    ///     The matching culture, or the current culture when the name is invalid.
    /// </summary>
    private static CultureInfo ResolveCulture(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentCulture;
        }
    }

    /// <summary>
    /// Composes a voice label for display in the interface.
    ///
    /// Args:
    ///     info: Metadados da voz instalada.
    ///
    /// Returns:
    ///     A label such as "Maria (Portuguese (Brazil))".
    /// </summary>
    private static string BuildDisplayName(VoiceInfo info) =>
        $"{info.Name} ({info.Culture.NativeName})";
}

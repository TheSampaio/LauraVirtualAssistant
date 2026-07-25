using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Speech.Synthesis;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Speech synthesizer backed by SAPI, the same engine that serves Windows voices.
/// </summary>
public sealed class SapiSpeechSynthesizer : ISpeechSynthesizer
{
    private const string PreferredEnglishVoice = "Microsoft Aria";

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
                .Where(static voice => IsSelectableNaturalVoice(voice.VoiceInfo))
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
    ///     ssml: Document to speak.
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
            _logger.LogError(exception, "SAPI rejected the generated SSML document.");
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
    ///     profile: Configured voice profile.
    ///     culture: Culture used to choose a voice when none was fixed.
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
    ///     culture: Desired culture.
    ///
    /// Returns:
    ///     The chosen voice name, or <see langword="null"/> when no voice
    ///     matches the culture and the choice should stay with the system.
    /// </summary>
    private string? FindPreferredVoiceName(CultureInfo culture)
    {
        IReadOnlyList<VoiceDescriptor> voices = GetAvailableVoices();
        const string preferredName = PreferredEnglishVoice;

        VoiceDescriptor? preferredNatural = voices.FirstOrDefault(voice =>
            voice.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

        if (preferredNatural is not null)
        {
            return preferredNatural.Name;
        }

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
    ///     cultureName: Culture name in BCP-47 format.
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
    ///     info: Installed voice metadata.
    ///
    /// Returns:
    ///     A label such as "Microsoft Aria (English (United States))".
    /// </summary>
    private static string BuildDisplayName(VoiceInfo info) =>
        $"{info.Name} ({info.Culture.NativeName})";

    private static bool IsSelectableNaturalVoice(VoiceInfo info)
    {
        if (!info.Name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return info.Name.Contains("Natural", StringComparison.OrdinalIgnoreCase)
            || info.Name.Contains(PreferredEnglishVoice, StringComparison.OrdinalIgnoreCase);
    }
}

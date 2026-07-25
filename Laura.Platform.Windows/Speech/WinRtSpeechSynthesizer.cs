using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Speech synthesizer backed by the Windows Runtime speech engine.
///
/// Unlike SAPI, which only exposes the old robotic "desktop" voices, the WinRT
/// engine also offers the OneCore and downloadable "Natural" neural voices (Aria,
/// Jenny, and friends) that sound far more human. The synthesized WAV stream is
/// played through NAudio, so no visual media element is needed.
/// </summary>
public sealed class WinRtSpeechSynthesizer : ISpeechSynthesizer
{
    private const double RatePerStep = 0.09;
    private const double PitchPerStep = 0.06;

    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly ILogger<WinRtSpeechSynthesizer> _logger;

    private WaveOutEvent? _output;
    private volatile bool _speaking;
    private bool _disposed;

    /// <summary>
    /// Initializes the synthesizer.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WinRtSpeechSynthesizer(ILogger<WinRtSpeechSynthesizer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSpeaking => _speaking;

    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices()
    {
        try
        {
            return [.. SpeechSynthesizer.AllVoices.Select(voice => new VoiceDescriptor(
                voice.Id,
                BuildDisplayName(voice),
                voice.Language,
                voice.Gender == VoiceGender.Female))];
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            _logger.LogError(exception, "Could not list the installed voices.");
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

        // Utterances are serialized: two at once would overlap in the audio device.
        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SelectVoice(request);
            ApplyVoiceProfile(request.Voice);

            using SpeechSynthesisStream stream = await _synthesizer
                .SynthesizeTextToStreamAsync(request.Text)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            await PlayAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NAudio.MmException)
        {
            // A synthesis or playback hiccup must not silence Laura permanently.
            _logger.LogWarning(exception, "The utterance could not be played.");
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
            _output?.Stop();
        }
        catch (NAudio.MmException)
        {
            // Playback was already stopping.
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
        _output?.Dispose();
        _synthesizer.Dispose();
        _speechGate.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Plays a synthesized WAV stream and waits for it to finish.
    ///
    /// The stream is copied into memory first so NAudio can seek the header freely,
    /// which the forward-only WinRT stream would not allow.
    ///
    /// Args:
    ///     synthStream: The synthesized audio stream.
    ///     cancellationToken: Token that stops playback.
    ///
    /// Returns:
    ///     A task that completes when playback ends or is cancelled.
    /// </summary>
    private async Task PlayAsync(SpeechSynthesisStream synthStream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        await using (Stream source = synthStream.AsStreamForRead())
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;

        using var reader = new WaveFileReader(buffer);
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, _) => completion.TrySetResult();
        output.Init(reader);

        _speaking = true;
        _output = output;

        await using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                output.Stop();
            }
            catch (NAudio.MmException)
            {
                // Ignored: the device is already stopping.
            }
        });

        try
        {
            output.Play();
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _speaking = false;
            _output = null;
        }
    }

    /// <summary>
    /// Selects the voice for the utterance, or the best default for the culture.
    ///
    /// Args:
    ///     request: The utterance, carrying the chosen voice and culture.
    /// </summary>
    private void SelectVoice(SpeechRequest request)
    {
        string? desired = request.Voice.VoiceName ?? FindPreferredVoiceId(request.Culture);

        if (desired is null)
        {
            return;
        }

        VoiceInformation? voice = SpeechSynthesizer.AllVoices
            .FirstOrDefault(candidate => string.Equals(candidate.Id, desired, StringComparison.Ordinal));

        if (voice is not null)
        {
            _synthesizer.Voice = voice;
        }
    }

    /// <summary>
    /// Applies rate, pitch and volume before the utterance.
    ///
    /// The WinRT engine controls all three natively, so pitch finally works — the
    /// desktop SAPI voices ignored the SSML pitch tag.
    ///
    /// Args:
    ///     profile: The configured voice profile.
    /// </summary>
    private void ApplyVoiceProfile(VoiceProfile profile)
    {
        _synthesizer.Options.AudioVolume = Math.Clamp(profile.Volume / 100.0, 0.0, 1.0);
        _synthesizer.Options.SpeakingRate = Math.Clamp(1.0 + (profile.Rate * RatePerStep), 0.5, 3.0);
        _synthesizer.Options.AudioPitch = Math.Clamp(1.0 + (profile.Pitch * PitchPerStep), 0.5, 2.0);
    }

    /// <summary>
    /// Picks the default voice for a culture, preferring a female voice.
    ///
    /// Laura is a female persona, so a female voice in the right language wins; then
    /// any voice of the language.
    ///
    /// Args:
    ///     cultureName: Desired culture, as a BCP-47 tag.
    ///
    /// Returns:
    ///     The chosen voice id, or <see langword="null"/> to leave the choice to the
    ///     system when no voice matches the language.
    /// </summary>
    private static string? FindPreferredVoiceId(string cultureName)
    {
        IReadOnlyList<VoiceInformation> voices = SpeechSynthesizer.AllVoices;
        string language = cultureName.Split('-')[0];

        bool MatchesLanguage(VoiceInformation voice) =>
            voice.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase);

        VoiceInformation? preferred =
            voices.FirstOrDefault(voice => MatchesLanguage(voice) && voice.Gender == VoiceGender.Female)
            ?? voices.FirstOrDefault(MatchesLanguage);

        return preferred?.Id;
    }

    /// <summary>
    /// Composes a voice's display label for the UI.
    ///
    /// Args:
    ///     voice: Metadata of the installed voice.
    ///
    /// Returns:
    ///     A label such as "Microsoft Aria (Natural) — English (United States)".
    /// </summary>
    private static string BuildDisplayName(VoiceInformation voice)
    {
        string culture = TryDescribeCulture(voice.Language);
        return culture.Length > 0 ? $"{voice.DisplayName} — {culture}" : voice.DisplayName;
    }

    /// <summary>
    /// Describes a language tag in its native name, tolerating unknown tags.
    ///
    /// Args:
    ///     language: BCP-47 language tag.
    ///
    /// Returns:
    ///     The native culture name, or an empty string when the tag is unknown.
    /// </summary>
    private static string TryDescribeCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }
}

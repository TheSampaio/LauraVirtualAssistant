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
    /// Inicializa o sintetizador e direciona a saída para o dispositivo padrão.
    ///
    /// Args:
    ///     logger: Destino dos registros de diagnóstico.
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
        Justification = "A interface precisa listar todas as vozes instaladas, não apenas as da cultura ativa.")]
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
            _logger.LogError(exception, "Não foi possível listar as vozes instaladas.");
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

        // As falas são serializadas: duas locuções simultâneas se atropelariam no SAPI.
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
            // O sintetizador já foi liberado; não há fala a interromper.
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
    /// Entrega o documento SSML ao SAPI e aguarda o fim da locução.
    ///
    /// A API do SAPI é baseada em eventos; a ponte para <c>async</c> é feita com um
    /// <see cref="TaskCompletionSource"/> assinado apenas durante esta locução.
    ///
    /// Args:
    ///     ssml: Documento a falar.
    ///     cancellationToken: Token que interrompe a locução.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a locução termina ou é interrompida.
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
            // Um texto que produza SSML inválido não pode calar Laura por completo.
            _logger.LogError(exception, "O SAPI recusou o documento SSML gerado.");
        }
        finally
        {
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
        }
    }

    /// <summary>
    /// Aplica ritmo, volume e escolha de voz antes da locução.
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
            // A voz salva pode ter sido desinstalada; a voz padrão do sistema atende.
            _logger.LogWarning(exception, "A voz {Voice} não está disponível.", desiredVoice);
            _selectedVoiceName = null;
        }
    }

    /// <summary>
    /// Escolhe a voz padrão para uma cultura.
    ///
    /// Laura é uma persona feminina, então uma voz feminina no idioma certo é
    /// preferida; só depois vem qualquer voz do idioma.
    ///
    /// Args:
    ///     culture: Cultura desejada.
    ///
    /// Returns:
    ///     O nome da voz escolhida, ou <see langword="null"/> quando nenhuma voz
    ///     atende a cultura e a escolha deve ficar com o sistema.
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
    /// Resolve o nome de cultura recebido, tolerando valores inválidos.
    ///
    /// Args:
    ///     cultureName: Nome da cultura no formato BCP-47.
    ///
    /// Returns:
    ///     A cultura correspondente, ou a cultura atual quando o nome é inválido.
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
    /// Compõe o rótulo de uma voz para exibição na interface.
    ///
    /// Args:
    ///     info: Metadados da voz instalada.
    ///
    /// Returns:
    ///     Um rótulo como "Maria (português (Brasil))".
    /// </summary>
    private static string BuildDisplayName(VoiceInfo info) =>
        $"{info.Name} ({info.Culture.NativeName})";
}

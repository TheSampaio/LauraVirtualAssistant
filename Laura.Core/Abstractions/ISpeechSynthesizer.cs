using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>
/// Motor de síntese de voz (text-to-speech).
///
/// Abstrai o motor concreto — SAPI no Windows hoje — para que o domínio não dependa
/// de nenhuma API de plataforma e possa ser exercitado com dublês em teste.
/// </summary>
public interface ISpeechSynthesizer : IAsyncDisposable
{
    /// <summary>
    /// Obtém um valor que indica se há fala em andamento neste momento.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Lista as vozes instaladas no sistema.
    ///
    /// Returns:
    ///     As vozes disponíveis, possivelmente vazia quando nenhum motor de
    ///     síntese está instalado.
    /// </summary>
    IReadOnlyList<VoiceDescriptor> GetAvailableVoices();

    /// <summary>
    /// Fala o texto do pedido e aguarda a locução terminar.
    ///
    /// Chamadas concorrentes são serializadas pela implementação: a segunda fala
    /// só começa quando a primeira termina.
    ///
    /// Args:
    ///     request: Texto e parâmetros de voz a aplicar.
    ///     cancellationToken: Token que interrompe a locução em andamento.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a locução termina ou é cancelada.
    /// </summary>
    Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrompe imediatamente a locução em andamento e descarta a fila de fala.
    /// </summary>
    void CancelSpeech();
}

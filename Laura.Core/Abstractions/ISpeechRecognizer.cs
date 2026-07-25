using Laura.Core.Configuration;
using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>
/// Motor de reconhecimento de fala (speech-to-text).
///
/// A implementação escuta em uma thread própria e publica transcrições por evento,
/// de modo que nem o motor da assistente nem a interface bloqueiem enquanto Laura ouve.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>
    /// Ocorre quando o motor produz uma transcrição.
    ///
    /// O evento é disparado em uma thread de segundo plano; assinantes que tocam a
    /// interface precisam marshalar para a thread de UI.
    /// </summary>
    event EventHandler<RecognitionResult>? Recognized;

    /// <summary>
    /// Obtém o modo de escuta ativo.
    /// </summary>
    RecognitionMode Mode { get; }

    /// <summary>
    /// Obtém um valor que indica se o motor pôde ser inicializado nesta máquina.
    ///
    /// É <see langword="false"/> quando não há microfone ou nenhum reconhecedor
    /// instalado; nesse caso Laura opera apenas por interface, sem voz.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Inicializa o motor e passa a escutar pelas frases de ativação.
    ///
    /// Args:
    ///     options: Frases de ativação, idioma e limiares de confiança.
    ///     cancellationToken: Token que aborta a inicialização.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o motor está escutando.
    /// </summary>
    Task StartAsync(RecognitionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrompe a escuta e libera o dispositivo de áudio.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a parada.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o motor deixou de escutar.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Alterna o modo de escuta, trocando a gramática carregada.
    ///
    /// Args:
    ///     mode: Modo desejado.
    ///     cancellationToken: Token que aborta a troca.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o novo modo está ativo.
    /// </summary>
    Task SetModeAsync(RecognitionMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica novas opções sem derrubar a sessão de escuta quando possível.
    ///
    /// Chamado quando o usuário altera idioma ou frases de ativação na interface.
    ///
    /// Args:
    ///     options: Opções atualizadas.
    ///     cancellationToken: Token que aborta a reconfiguração.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando as opções estão em vigor.
    /// </summary>
    Task ApplyOptionsAsync(RecognitionOptions options, CancellationToken cancellationToken = default);
}

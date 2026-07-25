using Laura.Core.Engine;

namespace Laura.Core.Abstractions;

/// <summary>
/// Motor da assistente: orquestra escuta, despacho de habilidades e fala.
/// </summary>
public interface IAssistantEngine : IAsyncDisposable
{
    /// <summary>
    /// Ocorre quando o estado da assistente muda.
    ///
    /// Disparado a partir do laço interno de processamento; assinantes de interface
    /// precisam marshalar para a thread de UI.
    /// </summary>
    event EventHandler<AssistantState>? StateChanged;

    /// <summary>
    /// Obtém o estado atual.
    /// </summary>
    AssistantState State { get; }

    /// <summary>
    /// Inicia o motor: carrega a escuta e faz a saudação de abertura.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a inicialização.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o motor está operante. A tarefa não
    ///     representa o tempo de vida do motor e retorna sem esperar pela fala.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Para a escuta e o processamento de comandos.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a parada.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o motor está parado.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia um comando em texto, como se tivesse sido ditado.
    ///
    /// Serve para a interface e para testes; não exige palavra de ativação.
    ///
    /// Args:
    ///     text: Comando a executar.
    ///     cancellationToken: Token que aborta a execução.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o comando terminou de ser processado.
    /// </summary>
    Task SubmitCommandAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Faz Laura dizer um texto, respeitando o perfil de voz configurado.
    ///
    /// Args:
    ///     text: Texto a falar.
    ///     cancellationToken: Token que interrompe a fala.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a fala termina.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}

using Laura.Core.Engine;

namespace Laura.Core.Abstractions;

/// <summary>
/// Assistant engine: orchestrates listening, skill dispatch, and speech.
/// </summary>
public interface IAssistantEngine : IAsyncDisposable
{
    /// <summary>
    /// Occurs when the assistant state changes.
    ///
    /// Raised from the internal processing loop; UI subscribers
    /// precisam marshalar para a thread de UI.
    /// </summary>
    event EventHandler<AssistantState>? StateChanged;

    /// <summary>
    /// Gets the current state.
    /// </summary>
    AssistantState State { get; }

    /// <summary>
    /// Starts the engine: loads listening and speaks the opening greeting.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts startup.
    ///
    /// Returns:
    ///     A task completed when the engine is operational. The task does not
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
    ///     A task completed when the engine is stopped.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a text command as if it had been dictated.
    ///
    /// Used by the interface and tests; does not require a wake word.
    ///
    /// Args:
    ///     text: Command to execute.
    ///     cancellationToken: Token that aborts execution.
    ///
    /// Returns:
    ///     A task completed when the command has finished processing.
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
    ///     A task completed when speech ends.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}

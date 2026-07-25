using System.Threading;

namespace Laura.App.Infrastructure;

/// <summary>
/// Implementação de <see cref="IUiDispatcher"/> sobre o <see cref="SynchronizationContext"/>
/// da thread da interface.
/// </summary>
public sealed class WinFormsUiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext _synchronizationContext;

    /// <summary>
    /// Captura o contexto de sincronização da thread atual.
    ///
    /// Deve ser construído na thread da interface, depois de o Windows Forms ter
    /// instalado seu contexto de sincronização.
    ///
    /// Raises:
    ///     InvalidOperationException: Quando não há contexto de interface ativo.
    /// </summary>
    public WinFormsUiDispatcher()
    {
        _synchronizationContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "O WinFormsUiDispatcher precisa ser criado na thread da interface.");
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _synchronizationContext.Post(static state => ((Action)state!).Invoke(), action);
    }
}

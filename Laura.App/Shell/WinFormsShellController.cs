using Laura.App.Infrastructure;
using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>
/// Implementação de <see cref="IShellController"/> que encaminha pedidos do domínio
/// para a camada Windows Forms, sempre na thread da interface.
///
/// As ações concretas são fornecidas pela camada de apresentação em
/// <see cref="Bind"/>, o que mantém o domínio ignorante do Windows Forms enquanto
/// ainda pode abrir a janela ou encerrar a aplicação.
/// </summary>
public sealed class WinFormsShellController : IShellController
{
    private readonly IUiDispatcher _dispatcher;

    private Action? _showSettings;
    private Action? _toggleSettings;
    private Action? _shutdown;

    /// <summary>
    /// Inicializa o controlador.
    ///
    /// Args:
    ///     dispatcher: Executor que garante a thread da interface.
    /// </summary>
    public WinFormsShellController(IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Associa as ações concretas da camada de apresentação.
    ///
    /// Args:
    ///     showSettings: Exibe a janela de configurações.
    ///     toggleSettings: Alterna a visibilidade da janela.
    ///     shutdown: Encerra a aplicação.
    /// </summary>
    public void Bind(Action showSettings, Action toggleSettings, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(toggleSettings);
        ArgumentNullException.ThrowIfNull(shutdown);

        _showSettings = showSettings;
        _toggleSettings = toggleSettings;
        _shutdown = shutdown;
    }

    /// <inheritdoc />
    public void ShowSettings() => _dispatcher.Post(() => _showSettings?.Invoke());

    /// <inheritdoc />
    public void ToggleSettings() => _dispatcher.Post(() => _toggleSettings?.Invoke());

    /// <inheritdoc />
    public void Shutdown() => _dispatcher.Post(() => _shutdown?.Invoke());
}

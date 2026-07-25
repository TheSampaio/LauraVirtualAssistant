using Laura.App.Infrastructure;
using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>
/// Implementation of <see cref="IShellController"/> that forwards domain requests
/// para a camada Windows Forms, sempre na thread da interface.
///
/// Concrete actions are provided by the presentation layer in
/// <see cref="Bind"/>, which keeps the domain unaware of Windows Forms while
/// still allowing it to open the window or shut down the application.
/// </summary>
public sealed class WinFormsShellController : IShellController
{
    private readonly IUiDispatcher _dispatcher;

    private Action? _showSettings;
    private Action? _toggleSettings;
    private Action? _shutdown;

    /// <summary>
    /// Initializes the controller.
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
    /// Binds the concrete actions from the presentation layer.
    ///
    /// Args:
    ///     showSettings: Shows the settings window.
    ///     toggleSettings: Toggles window visibility.
    ///     shutdown: Shuts down the application.
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

using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>Connects domain shell requests to the native MAUI window lifetime.</summary>
public sealed class MauiShellController : Laura.Core.Abstractions.IShellController
{
    private Action? _show;
    private Action? _toggle;
    private Action? _shutdown;
    /// <summary>Binds window actions after MAUI has created the native handle.</summary>
    public void Bind(Action show, Action toggle, Action shutdown) { _show = show; _toggle = toggle; _shutdown = shutdown; }
    /// <inheritdoc />
    public void ShowSettings() => MainThread.BeginInvokeOnMainThread(() => _show?.Invoke());
    /// <inheritdoc />
    public void ToggleSettings() => MainThread.BeginInvokeOnMainThread(() => _toggle?.Invoke());
    /// <inheritdoc />
    public void Shutdown() => MainThread.BeginInvokeOnMainThread(() => _shutdown?.Invoke());
}

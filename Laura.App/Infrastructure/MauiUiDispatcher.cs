namespace Laura.App.Infrastructure;

/// <summary>Marshals domain events to the MAUI UI thread.</summary>
public sealed class MauiUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
}

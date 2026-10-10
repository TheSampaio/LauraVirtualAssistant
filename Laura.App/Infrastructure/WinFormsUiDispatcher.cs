using System.Threading;

namespace Laura.App.Infrastructure;

/// <summary>
/// Implementation of <see cref="IUiDispatcher"/> over <see cref="SynchronizationContext"/>
/// on the presentation thread.
/// </summary>
public sealed class WinFormsUiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext _synchronizationContext;

    /// <summary>
    /// Captures the current thread synchronization context.
    ///
    /// Must be built on the UI thread, after Windows Forms has
    /// installed its synchronization context.
    ///
    /// Raises:
    ///     InvalidOperationException: When there is no active UI context.
    /// </summary>
    public WinFormsUiDispatcher()
    {
        _synchronizationContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "WinFormsUiDispatcher must be created on the UI thread.");
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _synchronizationContext.Post(static state => ((Action)state!).Invoke(), action);
    }
}

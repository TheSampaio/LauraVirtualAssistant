namespace Laura.App.Infrastructure;

/// <summary>
/// Dispatches work to the MAUI UI thread when domain events arrive from background workers.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Schedules an action to run on the UI thread without waiting for it.
    ///
    /// Args:
    ///     action: Work to execute on the UI thread.
    /// </summary>
    void Post(Action action);
}

namespace Laura.App.Infrastructure;

/// <summary>
/// Executa trabalho na thread da interface.
///
/// Assistant engine events arrive on background threads; touching
/// Windows Forms controls from them would throw, so everything that changes the
/// interface passa por aqui.
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

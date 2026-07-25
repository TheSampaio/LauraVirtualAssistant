namespace Laura.Core.Abstractions;

/// <summary>
/// Controls the application visual layer from the domain.
///
/// Allows a skill to open the settings panel or the engine to request
/// shutdown without <c>Laura.Core</c> knowing about WPF.
/// </summary>
public interface IShellController
{
    /// <summary>
    /// Shows the settings panel and brings the window to the front.
    /// </summary>
    void ShowSettings();

    /// <summary>
    /// Toggles settings panel visibility.
    ///
    /// This is what the global hotkey triggers.
    /// </summary>
    void ToggleSettings();

    /// <summary>
    /// Shuts down the application cleanly.
    /// </summary>
    void Shutdown();
}

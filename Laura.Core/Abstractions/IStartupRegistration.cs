namespace Laura.Core.Abstractions;

/// <summary>
/// Registers or removes the application from system automatic startup.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// Reports whether the application is registered to start with the system.
    ///
    /// Returns:
    ///     <see langword="true"/> when the registration exists.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Creates or removes the automatic startup entry.
    ///
    /// Args:
    ///     enabled: <see langword="true"/> to register, <see langword="false"/>
    ///     to remove.
    /// </summary>
    void SetEnabled(bool enabled);
}

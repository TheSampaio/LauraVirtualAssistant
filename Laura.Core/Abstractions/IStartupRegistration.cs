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
    ///     <see langword="true"/> quando o registro existe.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Creates or removes the automatic startup entry.
    ///
    /// Args:
    ///     enabled: <see langword="true"/> para registrar, <see langword="false"/>
    ///     to remove.
    /// </summary>
    void SetEnabled(bool enabled);
}

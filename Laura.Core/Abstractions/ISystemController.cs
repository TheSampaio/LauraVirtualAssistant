namespace Laura.Core.Abstractions;

/// <summary>
/// Actions on the operating system session that Laura can execute.
/// </summary>
public interface ISystemController
{
    /// <summary>
    /// Requests a workstation lock and reports whether the operating system accepted the request.
    /// </summary>
    bool LockWorkstation();

    /// <summary>
    /// Adjusts the master volume in steps.
    ///
    /// Args:
    ///     steps: Number of increments; negative values reduce the volume.
    /// </summary>
    void AdjustVolume(int steps);

    /// <summary>
    /// Toggles master volume muting.
    /// </summary>
    void ToggleMute();
}

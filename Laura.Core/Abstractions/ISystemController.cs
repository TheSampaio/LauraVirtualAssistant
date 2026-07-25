namespace Laura.Core.Abstractions;

/// <summary>
/// Actions on the operating system session that Laura can execute.
/// </summary>
public interface ISystemController
{
    /// <summary>
    /// Locks the workstation.
    /// </summary>
    void LockWorkstation();

    /// <summary>
    /// Ajusta o volume principal em passos.
    ///
    /// Args:
    ///     steps: Number of increments; negative values reduce the volume.
    /// </summary>
    void AdjustVolume(int steps);

    /// <summary>
    /// Alterna o silenciamento do volume principal.
    /// </summary>
    void ToggleMute();
}

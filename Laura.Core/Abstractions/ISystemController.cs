namespace Laura.Core.Abstractions;

/// <summary>
/// Ações sobre a sessão do sistema operacional que Laura sabe executar.
/// </summary>
public interface ISystemController
{
    /// <summary>
    /// Bloqueia a estação de trabalho.
    /// </summary>
    void LockWorkstation();

    /// <summary>
    /// Ajusta o volume principal em passos.
    ///
    /// Args:
    ///     steps: Número de incrementos; valores negativos reduzem o volume.
    /// </summary>
    void AdjustVolume(int steps);

    /// <summary>
    /// Alterna o silenciamento do volume principal.
    /// </summary>
    void ToggleMute();
}

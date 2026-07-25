namespace Laura.Core.Abstractions;

/// <summary>
/// Registra ou remove a aplicação da inicialização automática do sistema.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// Informa se a aplicação está registrada para iniciar com o sistema.
    ///
    /// Returns:
    ///     <see langword="true"/> quando o registro existe.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Cria ou remove o registro de inicialização automática.
    ///
    /// Args:
    ///     enabled: <see langword="true"/> para registrar, <see langword="false"/>
    ///     para remover.
    /// </summary>
    void SetEnabled(bool enabled);
}

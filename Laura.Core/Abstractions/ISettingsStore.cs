using Laura.Core.Configuration;

namespace Laura.Core.Abstractions;

/// <summary>
/// Persistência das configurações da assistente.
///
/// Separado de <see cref="ISettingsService"/> para que o meio de armazenamento
/// (arquivo, registro, nuvem) mude sem afetar quem apenas lê ou observa configurações.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Carrega as configurações persistidas.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a leitura.
    ///
    /// Returns:
    ///     As configurações salvas, ou <see cref="LauraSettings.Default"/> quando
    ///     ainda não existem ou o conteúdo está corrompido.
    /// </summary>
    Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava as configurações de forma atômica.
    ///
    /// Args:
    ///     settings: Configurações a persistir.
    ///     cancellationToken: Token que aborta a gravação.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a gravação termina.
    /// </summary>
    Task SaveAsync(LauraSettings settings, CancellationToken cancellationToken = default);
}

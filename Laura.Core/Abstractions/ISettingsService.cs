using Laura.Core.Configuration;

namespace Laura.Core.Abstractions;

/// <summary>
/// Ponto único de acesso às configurações em memória.
///
/// Publica <see cref="Changed"/> para que motor de fala, escuta e interface reajam
/// no ato a uma alteração — trocar a voz ou o tom não deve exigir reiniciar Laura.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Ocorre depois que as configurações são substituídas, carregando o novo estado.
    /// </summary>
    event EventHandler<LauraSettings>? Changed;

    /// <summary>
    /// Obtém as configurações vigentes.
    /// </summary>
    LauraSettings Current { get; }

    /// <summary>
    /// Carrega as configurações do armazenamento e as publica.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a carga.
    ///
    /// Returns:
    ///     As configurações carregadas.
    /// </summary>
    Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Substitui, persiste e publica as configurações.
    ///
    /// Args:
    ///     settings: Novas configurações; são saneadas antes de valer.
    ///     cancellationToken: Token que aborta a gravação.
    ///
    /// Returns:
    ///     As configurações efetivamente aplicadas, após saneamento.
    /// </summary>
    Task<LauraSettings> UpdateAsync(LauraSettings settings, CancellationToken cancellationToken = default);
}

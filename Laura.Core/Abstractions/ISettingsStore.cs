using Laura.Core.Configuration;

namespace Laura.Core.Abstractions;

/// <summary>
/// Persistence for assistant settings.
///
/// Separado de <see cref="ISettingsService"/> para que o meio de armazenamento
/// (file, registry, cloud) can change without affecting readers or observers of settings.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads persisted settings.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a leitura.
    ///
    /// Returns:
    ///     The saved settings, or <see cref="LauraSettings.Default"/> when
    ///     they do not exist yet or the content is corrupted.
    /// </summary>
    Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes settings atomically.
    ///
    /// Args:
    ///     settings: Settings to persist.
    ///     cancellationToken: Token that aborts the write.
    ///
    /// Returns:
    ///     A task completed when writing finishes.
    /// </summary>
    Task SaveAsync(LauraSettings settings, CancellationToken cancellationToken = default);
}

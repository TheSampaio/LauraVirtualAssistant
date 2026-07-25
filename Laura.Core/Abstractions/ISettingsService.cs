using Laura.Core.Configuration;

namespace Laura.Core.Abstractions;

/// <summary>
/// Single access point for in-memory settings.
///
/// Publica <see cref="Changed"/> para que motor de fala, escuta e interface reajam
/// immediately to a change - changing the voice or pitch should not require restarting Laura.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Occurs after settings are replaced, carrying the new state.
    /// </summary>
    event EventHandler<LauraSettings>? Changed;

    /// <summary>
    /// Gets the current settings.
    /// </summary>
    LauraSettings Current { get; }

    /// <summary>
    /// Loads settings from storage and publishes them.
    ///
    /// Args:
    ///     cancellationToken: Token que aborta a carga.
    ///
    /// Returns:
    ///     The loaded settings.
    /// </summary>
    Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces, persists, and publishes settings.
    ///
    /// Args:
    ///     settings: New settings; sanitized before taking effect.
    ///     cancellationToken: Token that aborts the write.
    ///
    /// Returns:
    ///     The settings effectively applied after sanitization.
    /// </summary>
    Task<LauraSettings> UpdateAsync(LauraSettings settings, CancellationToken cancellationToken = default);
}

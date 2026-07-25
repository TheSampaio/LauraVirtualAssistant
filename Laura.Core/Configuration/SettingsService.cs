using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Configuration;

/// <summary>
/// Implementation of <see cref="ISettingsService"/> over an <see cref="ISettingsStore"/>.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private LauraSettings _current = LauraSettings.Default;

    /// <summary>
    /// Initializes the service with the given store and logger.
    ///
    /// Args:
    ///     store: Storage where settings are read and written.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public SettingsService(ISettingsStore store, ILogger<SettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<LauraSettings>? Changed;

    /// <inheritdoc />
    public LauraSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public async Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        LauraSettings loaded = (await _store.LoadAsync(cancellationToken).ConfigureAwait(false)).Sanitized();
        Publish(loaded);

        _logger.LogInformation("Settings loaded (culture {Culture}).", loaded.Culture);
        return loaded;
    }

    /// <inheritdoc />
    public async Task<LauraSettings> UpdateAsync(LauraSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        LauraSettings sanitized = settings.Sanitized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _store.SaveAsync(sanitized, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        Publish(sanitized);

        _logger.LogInformation("Settings updated (culture {Culture}).", sanitized.Culture);
        return sanitized;
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();

    /// <summary>
    /// Makes the settings current and notifies subscribers.
    ///
    /// Args:
    ///     settings: Already-sanitized settings.
    /// </summary>
    private void Publish(LauraSettings settings)
    {
        Volatile.Write(ref _current, settings);
        Changed?.Invoke(this, settings);
    }
}

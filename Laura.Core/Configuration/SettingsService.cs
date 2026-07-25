using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Configuration;

/// <summary>
/// Implementação de <see cref="ISettingsService"/> sobre um <see cref="ISettingsStore"/>.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private LauraSettings _current = LauraSettings.Default;

    /// <summary>
    /// Inicializa o serviço com o armazenamento e o registrador informados.
    ///
    /// Args:
    ///     store: Armazenamento onde as configurações são lidas e gravadas.
    ///     logger: Destino dos registros de diagnóstico.
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

        _logger.LogInformation("Configurações carregadas (cultura {Culture}).", loaded.Culture);
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

        _logger.LogInformation("Configurações atualizadas (cultura {Culture}).", sanitized.Culture);
        return sanitized;
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();

    /// <summary>
    /// Torna as configurações vigentes e notifica os assinantes.
    ///
    /// Args:
    ///     settings: Configurações já saneadas.
    /// </summary>
    private void Publish(LauraSettings settings)
    {
        Volatile.Write(ref _current, settings);
        Changed?.Invoke(this, settings);
    }
}

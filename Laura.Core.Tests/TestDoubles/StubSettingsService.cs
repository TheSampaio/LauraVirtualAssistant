using Laura.Core.Abstractions;
using Laura.Core.Configuration;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test settings service with a fixed in-memory value.
/// </summary>
public sealed class StubSettingsService : ISettingsService
{
    /// <summary>
    /// Creates the service with the given settings.
    ///
    /// Args:
    ///     settings: Settings returned by <see cref="Current"/>.
    /// </summary>
    public StubSettingsService(LauraSettings settings) => Current = settings;

    /// <inheritdoc />
    public event EventHandler<LauraSettings>? Changed;

    /// <inheritdoc />
    public LauraSettings Current { get; private set; }

    /// <inheritdoc />
    public Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    /// <inheritdoc />
    public Task<LauraSettings> UpdateAsync(LauraSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        return Task.FromResult(settings);
    }
}

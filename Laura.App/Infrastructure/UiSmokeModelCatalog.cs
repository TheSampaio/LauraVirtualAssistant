using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Models;

namespace Laura.App.Infrastructure;

/// <summary>Deterministic account-list fixtures for the explicitly isolated UI harness; never contacts a real provider.</summary>
internal sealed class UiSmokeModelCatalog : IProviderModelCatalog
{
    /// <inheritdoc />
    public Task<ProviderModelCatalogue> ListAsync(string provider, string credential, bool refresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Synthetic model metadata requires isolated UI smoke mode."); }
        if (string.IsNullOrWhiteSpace(credential)) { return Task.FromResult(new ProviderModelCatalogue(provider, [], null, true, "key_required")); }
        string[] models = [GeminiLiveOptions.Model, GeminiLiveOptions.FallbackModel];
        return Task.FromResult(new ProviderModelCatalogue(provider, models.Select(id => new ProviderModel(id, id,
            ProviderModelCompatibility.Gemini(id, new HashSet<string>()))).ToArray(), DateTimeOffset.UtcNow, false, null));
    }
}

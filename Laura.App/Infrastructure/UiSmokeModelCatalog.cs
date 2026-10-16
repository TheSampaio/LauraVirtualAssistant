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
        string[] models = provider == "gemini" ? [GeminiLiveOptions.Model, GeminiLiveOptions.FallbackModel] : ["gpt-5.4-mini", "o3"];
        return Task.FromResult(new ProviderModelCatalogue(provider, models.Select(id => new ProviderModel(id, id,
            provider == "gemini" ? ProviderModelCompatibility.Gemini(id, new HashSet<string>()) : ProviderModelCompatibility.OpenAi(id))).ToArray(), DateTimeOffset.UtcNow, false, null));
    }
}

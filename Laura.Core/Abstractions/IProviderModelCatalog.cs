using Laura.Core.Models;

namespace Laura.Core.Abstractions;

/// <summary>Discovers account-visible provider models without sending conversation data.</summary>
public interface IProviderModelCatalog
{
    /// <summary>Queries the fixed provider endpoint or a credential-isolated cache. Args: provider: Native provider ID; credential: In-memory API key; refresh: Bypass fresh cache; cancellationToken: Stop signal. Returns: Actual catalogue with stale/failure metadata.</summary>
    Task<ProviderModelCatalogue> ListAsync(string provider, string credential, bool refresh = false, CancellationToken cancellationToken = default);
}

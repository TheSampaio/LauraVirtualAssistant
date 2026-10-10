using Laura.Core.Models;

namespace Laura.Core.Abstractions;

/// <summary>Laura-owned model storage; weights remain separate from application binaries.</summary>
public interface ILocalModelManager : IModelCatalog
{
    /// <summary>Gets the reviewed download catalog.</summary>
    IReadOnlyList<ModelArtifact> Catalog { get; }
    /// <summary>Resolves a managed model id to its local path, rejecting unsafe names.</summary>
    string GetModelPath(string id);
    /// <summary>Installs with length/hash verification. Interrupted downloads can be retried.</summary>
    Task InstallAsync(ModelArtifact artifact, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken);
    /// <summary>Deletes only the chosen model and partial download from managed storage.</summary>
    void Delete(string id);
}

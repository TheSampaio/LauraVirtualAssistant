namespace Laura.Core.Abstractions;

/// <summary>
/// Lists the generative models available to be selected in the UI.
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// Enumerates the models installed locally.
    ///
    /// Returns:
    ///     The installed model names (such as <c>llama3.2:latest</c>), or an empty
    ///     list when the provider is not installed or has no models pulled.
    /// </summary>
    IReadOnlyList<string> GetInstalledModels();
}

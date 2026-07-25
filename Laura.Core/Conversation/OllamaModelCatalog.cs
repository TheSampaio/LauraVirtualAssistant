using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Conversation;

/// <summary>
/// Discovers the models Ollama has pulled by reading its manifest directory.
///
/// Ollama stores one folder per model and one file per tag under
/// <c>&lt;user&gt;/.ollama/models/manifests/registry.ollama.ai/library</c>. Reading
/// the folder is far cheaper than starting the Ollama server just to list models,
/// and it works even when the server is not running.
/// </summary>
public sealed class OllamaModelCatalog : IModelCatalog
{
    private readonly string _libraryDirectory;
    private readonly ILogger<OllamaModelCatalog> _logger;

    /// <summary>
    /// Initializes the catalog pointing at Ollama's manifest library.
    ///
    /// Args:
    ///     libraryDirectory: Path of the <c>library</c> manifest folder.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public OllamaModelCatalog(string libraryDirectory, ILogger<OllamaModelCatalog> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryDirectory = libraryDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the default Ollama manifest library path for the current user.
    ///
    /// The user profile is resolved dynamically, so it is correct on any machine
    /// and for any account.
    ///
    /// Returns:
    ///     The full path of the Ollama <c>library</c> manifest folder.
    /// </summary>
    public static string GetDefaultLibraryDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ollama",
        "models",
        "manifests",
        "registry.ollama.ai",
        "library");

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledModels()
    {
        if (!Directory.Exists(_libraryDirectory))
        {
            _logger.LogInformation("Ollama library not found at {Path}; no models to list.", _libraryDirectory);
            return [];
        }

        try
        {
            List<string> models = [];

            foreach (string modelDirectory in Directory.EnumerateDirectories(_libraryDirectory))
            {
                string modelName = Path.GetFileName(modelDirectory);

                foreach (string tagFile in Directory.EnumerateFiles(modelDirectory))
                {
                    string tag = Path.GetFileName(tagFile);
                    models.Add($"{modelName}:{tag}");
                }
            }

            models.Sort(StringComparer.OrdinalIgnoreCase);
            return models;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Failed to read the Ollama library at {Path}.", _libraryDirectory);
            return [];
        }
    }
}

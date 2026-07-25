using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Localization;

/// <summary>
/// Localizer that reads text from JSON files, one per culture.
///
/// Each key accepts a single string or a string list; lists are used both
/// for alternate response wording and for a skill's triggers.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    private const string FallbackCultureName = "en-US";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> _catalogs;
    private readonly ILogger<JsonLocalizer> _logger;

    private IReadOnlyDictionary<string, IReadOnlyList<string>> _active;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _fallback;

    /// <summary>
    /// Initializes the localizer by loading every language file in the folder.
    ///
    /// Args:
    ///     localesDirectory: Pasta com os arquivos <c>&lt;cultura&gt;.json</c>.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public JsonLocalizer(string localesDirectory, ILogger<JsonLocalizer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localesDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _catalogs = LoadCatalogs(localesDirectory, logger);

        AvailableCultures = [.. _catalogs.Keys
            .Select(static name => CultureInfo.GetCultureInfo(name))
            .OrderBy(static culture => culture.NativeName, StringComparer.CurrentCulture)];

        _fallback = _catalogs.GetValueOrDefault(FallbackCultureName) ?? _catalogs.Values.First();
        _active = _fallback;

        Culture = AvailableCultures.FirstOrDefault(static culture => culture.Name == FallbackCultureName)
            ?? AvailableCultures[0];
    }

    /// <inheritdoc />
    public event EventHandler<CultureInfo>? CultureChanged;

    /// <inheritdoc />
    public CultureInfo Culture { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <inheritdoc />
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo resolved = ResolveSupportedCulture(culture);

        if (resolved.Name == Culture.Name)
        {
            return;
        }

        Culture = resolved;
        _active = _catalogs[resolved.Name];

        _logger.LogInformation("Language changed to {Culture}.", resolved.Name);
        CultureChanged?.Invoke(this, resolved);
    }

    /// <inheritdoc />
    public string Get(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        IReadOnlyList<string> variants = Lookup(key);

        if (variants.Count == 0)
        {
            _logger.LogWarning("Texto ausente para a chave {Key} em {Culture}.", key, Culture.Name);
            return $"[{key}]";
        }

        string template = variants.Count == 1 ? variants[0] : variants[Random.Shared.Next(variants.Count)];

        return arguments.Length == 0
            ? template
            : string.Format(Culture, template, arguments);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetPhrases(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Lookup(key);
    }

    /// <summary>
    /// Looks up a key in the active culture and, when needed, in the resource culture.
    ///
    /// Args:
    ///     key: Chave a procurar.
    ///
    /// Returns:
    ///     The registered values, or an empty list when the key does not exist
    ///     in either catalog.
    /// </summary>
    private IReadOnlyList<string> Lookup(string key)
    {
        if (_active.TryGetValue(key, out IReadOnlyList<string>? values) && values.Count > 0)
        {
            return values;
        }

        return _fallback.TryGetValue(key, out IReadOnlyList<string>? fallbackValues)
            ? fallbackValues
            : [];
    }

    /// <summary>
    /// Chooses the supported culture closest to the requested one.
    ///
    /// Tries an exact match, then any culture with the same language
    /// (pt-PT atende um pedido de pt-BR) e, por fim, a cultura de recurso.
    ///
    /// Args:
    ///     requested: Cultura solicitada.
    ///
    /// Returns:
    ///     A culture for which a catalog is loaded.
    /// </summary>
    private CultureInfo ResolveSupportedCulture(CultureInfo requested)
    {
        if (_catalogs.ContainsKey(requested.Name))
        {
            return requested;
        }

        CultureInfo? sameLanguage = AvailableCultures.FirstOrDefault(
            candidate => candidate.TwoLetterISOLanguageName.Equals(
                requested.TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase));

        if (sameLanguage is not null)
        {
            _logger.LogInformation(
                "Culture {Requested} not found; using {Resolved}.",
                requested.Name,
                sameLanguage.Name);

            return sameLanguage;
        }

        return Culture;
    }

    /// <summary>
    /// Reads every language file from the given folder.
    ///
    /// Args:
    ///     directory: Pasta com os arquivos <c>&lt;cultura&gt;.json</c>.
    ///     logger: Destination for diagnostic logs.
    ///
    /// Returns:
    ///     One catalog per culture, indexed by the culture's BCP-47 name.
    ///
    /// Raises:
    ///     InvalidOperationException: When no valid language file is
    ///     found - without text, Laura would have nothing to say.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogs(
        string directory,
        ILogger logger)
    {
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> catalogs =
            new(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Language folder not found: {directory}");
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            string cultureName = Path.GetFileNameWithoutExtension(path);

            try
            {
                _ = CultureInfo.GetCultureInfo(cultureName);

                using FileStream stream = File.OpenRead(path);
                Dictionary<string, JsonElement>? raw = JsonSerializer
                    .Deserialize<Dictionary<string, JsonElement>>(stream, SerializerOptions);

                if (raw is null)
                {
                    continue;
                }

                catalogs[cultureName] = raw.ToDictionary(
                    static entry => entry.Key,
                    static entry => ReadValues(entry.Value),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is JsonException or CultureNotFoundException or IOException)
            {
                logger.LogWarning(exception, "Language file ignored: {Path}.", path);
            }
        }

        return catalogs.Count > 0
            ? catalogs
            : throw new InvalidOperationException($"No valid language file in: {directory}");
    }

    /// <summary>
    /// Converts a language file value into a string list.
    ///
    /// Args:
    ///     element: JSON value, single string, or string array.
    ///
    /// Returns:
    ///     The values as a list; an empty list for unsupported types.
    /// </summary>
    private static IReadOnlyList<string> ReadValues(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString() ?? string.Empty],
        JsonValueKind.Array => [.. element.EnumerateArray()
            .Where(static item => item.ValueKind is JsonValueKind.String)
            .Select(static item => item.GetString() ?? string.Empty)
            .Where(static value => value.Length > 0)],
        _ => [],
    };
}

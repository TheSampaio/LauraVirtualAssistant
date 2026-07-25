using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Localization;

/// <summary>
/// Localizador que lê os textos de arquivos JSON, um por cultura.
///
/// Cada chave aceita uma string única ou uma lista de strings; listas servem tanto
/// para redações alternativas de uma resposta quanto para os gatilhos de uma habilidade.
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
    /// Inicializa o localizador carregando todos os arquivos de idioma da pasta.
    ///
    /// Args:
    ///     localesDirectory: Pasta com os arquivos <c>&lt;cultura&gt;.json</c>.
    ///     logger: Destino dos registros de diagnóstico.
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

        _logger.LogInformation("Idioma alterado para {Culture}.", resolved.Name);
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
    /// Busca uma chave na cultura ativa e, se necessário, na cultura de recurso.
    ///
    /// Args:
    ///     key: Chave a procurar.
    ///
    /// Returns:
    ///     Os valores cadastrados, ou uma lista vazia quando a chave não existe
    ///     em nenhum dos dois catálogos.
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
    /// Escolhe a cultura suportada mais próxima da solicitada.
    ///
    /// Tenta a correspondência exata, depois qualquer cultura com o mesmo idioma
    /// (pt-PT atende um pedido de pt-BR) e, por fim, a cultura de recurso.
    ///
    /// Args:
    ///     requested: Cultura solicitada.
    ///
    /// Returns:
    ///     Uma cultura para a qual existe catálogo carregado.
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
                "Cultura {Requested} não encontrada; usando {Resolved}.",
                requested.Name,
                sameLanguage.Name);

            return sameLanguage;
        }

        return Culture;
    }

    /// <summary>
    /// Lê todos os arquivos de idioma da pasta informada.
    ///
    /// Args:
    ///     directory: Pasta com os arquivos <c>&lt;cultura&gt;.json</c>.
    ///     logger: Destino dos registros de diagnóstico.
    ///
    /// Returns:
    ///     Um catálogo por cultura, indexado pelo nome BCP-47 da cultura.
    ///
    /// Raises:
    ///     InvalidOperationException: Quando nenhum arquivo de idioma válido é
    ///     encontrado — sem textos, Laura não teria o que dizer.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogs(
        string directory,
        ILogger logger)
    {
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> catalogs =
            new(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Pasta de idiomas não encontrada: {directory}");
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
                logger.LogWarning(exception, "Arquivo de idioma ignorado: {Path}.", path);
            }
        }

        return catalogs.Count > 0
            ? catalogs
            : throw new InvalidOperationException($"Nenhum arquivo de idioma válido em: {directory}");
    }

    /// <summary>
    /// Converte um valor do arquivo de idioma em lista de strings.
    ///
    /// Args:
    ///     element: Valor JSON, string única ou vetor de strings.
    ///
    /// Returns:
    ///     Os valores como lista; uma lista vazia para tipos não suportados.
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

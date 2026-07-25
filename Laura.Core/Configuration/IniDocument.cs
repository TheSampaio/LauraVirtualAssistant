using System.Globalization;
using System.Text;

namespace Laura.Core.Configuration;

/// <summary>
/// Leitor e escritor de arquivos INI.
///
/// O formato foi escolhido por ser legível e editável à mão sem ferramenta nenhuma —
/// abrir o arquivo no Bloco de Notas e corrigir um valor é parte do fluxo esperado.
/// Chaves e seções não diferenciam maiúsculas de minúsculas.
/// </summary>
public sealed class IniDocument
{
    private const char SectionOpen = '[';
    private const char SectionClose = ']';
    private const char KeyValueSeparator = '=';
    private static readonly char[] CommentMarkers = [';', '#'];

    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Interpreta o conteúdo de um arquivo INI.
    ///
    /// Linhas malformadas são ignoradas em vez de interromper a leitura: uma edição
    /// manual desastrada não deve impedir Laura de iniciar.
    ///
    /// Args:
    ///     content: Texto completo do arquivo.
    ///
    /// Returns:
    ///     Um documento com as seções e chaves encontradas.
    /// </summary>
    public static IniDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = new IniDocument();
        string currentSection = string.Empty;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || CommentMarkers.Contains(line[0]))
            {
                continue;
            }

            if (line[0] == SectionOpen && line[^1] == SectionClose)
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            int separatorIndex = line.IndexOf(KeyValueSeparator, StringComparison.Ordinal);

            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            document.Set(currentSection, key, value);
        }

        return document;
    }

    /// <summary>
    /// Define o valor de uma chave, criando a seção quando necessário.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     value: Valor a gravar.
    /// </summary>
    public void Set(string section, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_sections.TryGetValue(section, out Dictionary<string, string>? entries))
        {
            entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = entries;
        }

        entries[key] = value ?? string.Empty;
    }

    /// <summary>
    /// Define uma chave a partir de um valor booleano.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     value: Valor a gravar.
    /// </summary>
    public void SetBoolean(string section, string key, bool value) =>
        Set(section, key, value ? "true" : "false");

    /// <summary>
    /// Define uma chave a partir de um número, sempre na cultura invariante.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     value: Valor a gravar.
    /// </summary>
    public void SetNumber(string section, string key, double value) =>
        Set(section, key, value.ToString("0.####", CultureInfo.InvariantCulture));

    /// <summary>
    /// Define uma chave a partir de uma lista, separando os itens por barra vertical.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     values: Itens a gravar.
    /// </summary>
    public void SetList(string section, string key, IEnumerable<string> values) =>
        Set(section, key, string.Join(" | ", values));

    /// <summary>
    /// Lê o valor de uma chave.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     fallback: Valor devolvido quando a chave não existe.
    ///
    /// Returns:
    ///     O valor gravado, ou o valor de recuo.
    /// </summary>
    public string GetString(string section, string key, string fallback) =>
        _sections.TryGetValue(section, out Dictionary<string, string>? entries)
        && entries.TryGetValue(key, out string? value)
        && value.Length > 0
            ? value
            : fallback;

    /// <summary>
    /// Lê uma chave como booleano.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     fallback: Valor devolvido quando a chave falta ou é inválida.
    ///
    /// Returns:
    ///     O booleano lido, ou o valor de recuo.
    /// </summary>
    public bool GetBoolean(string section, string key, bool fallback) =>
        bool.TryParse(GetString(section, key, string.Empty), out bool value) ? value : fallback;

    /// <summary>
    /// Lê uma chave como inteiro.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     fallback: Valor devolvido quando a chave falta ou é inválida.
    ///
    /// Returns:
    ///     O inteiro lido, ou o valor de recuo.
    /// </summary>
    public int GetInt32(string section, string key, int fallback) =>
        int.TryParse(
            GetString(section, key, string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    /// <summary>
    /// Lê uma chave como número de ponto flutuante.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     fallback: Valor devolvido quando a chave falta ou é inválida.
    ///
    /// Returns:
    ///     O número lido, ou o valor de recuo.
    /// </summary>
    public double GetDouble(string section, string key, double fallback) =>
        double.TryParse(
            GetString(section, key, string.Empty),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : fallback;

    /// <summary>
    /// Lê uma chave como lista separada por barra vertical.
    ///
    /// Args:
    ///     section: Nome da seção.
    ///     key: Nome da chave.
    ///     fallback: Lista devolvida quando a chave falta ou está vazia.
    ///
    /// Returns:
    ///     Os itens lidos, ou a lista de recuo.
    /// </summary>
    public IReadOnlyList<string> GetList(string section, string key, IReadOnlyList<string> fallback)
    {
        string raw = GetString(section, key, string.Empty);

        if (raw.Length == 0)
        {
            return fallback;
        }

        List<string> items = [.. raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        return items.Count > 0 ? items : fallback;
    }

    /// <summary>
    /// Serializa o documento no formato INI.
    ///
    /// Args:
    ///     header: Comentário opcional inserido no topo do arquivo.
    ///
    /// Returns:
    ///     O texto pronto para gravação.
    /// </summary>
    public string ToIniString(string? header = null)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(header))
        {
            foreach (string line in header.Split('\n'))
            {
                builder.Append("; ").AppendLine(line.TrimEnd());
            }

            builder.AppendLine();
        }

        foreach ((string section, Dictionary<string, string> entries) in _sections)
        {
            if (section.Length > 0)
            {
                builder.Append(SectionOpen).Append(section).Append(SectionClose).AppendLine();
            }

            foreach ((string key, string value) in entries)
            {
                builder.Append(key).Append(' ').Append(KeyValueSeparator).Append(' ').AppendLine(value);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}

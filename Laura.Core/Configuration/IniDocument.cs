using System.Globalization;
using System.Text;

namespace Laura.Core.Configuration;

/// <summary>
/// Leitor e escritor de arquivos INI.
///
/// The format was chosen because it is readable and editable by hand with no tools -
/// opening the file in Notepad and correcting a value is part of the expected flow.
/// Keys and sections are case-insensitive.
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
    /// Parses the contents of an INI file.
    ///
    /// Malformed lines are ignored instead of interrupting reads: a clumsy manual
    /// edit should not prevent Laura from starting.
    ///
    /// Args:
    ///     content: Complete file text.
    ///
    /// Returns:
    ///     A document with the sections and keys found.
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
    /// Sets a key value, creating the section when needed.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
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
    /// Sets a key from a boolean value.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     value: Valor a gravar.
    /// </summary>
    public void SetBoolean(string section, string key, bool value) =>
        Set(section, key, value ? "true" : "false");

    /// <summary>
    /// Sets a key from a number, always using the invariant culture.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     value: Valor a gravar.
    /// </summary>
    public void SetNumber(string section, string key, double value) =>
        Set(section, key, value.ToString("0.####", CultureInfo.InvariantCulture));

    /// <summary>
    /// Sets a key from a list, separating items with a pipe.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     values: Itens a gravar.
    /// </summary>
    public void SetList(string section, string key, IEnumerable<string> values) =>
        Set(section, key, string.Join(" | ", values));

    /// <summary>
    /// Reads a key value.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     fallback: Value returned when the key does not exist.
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
    /// Reads a key as a boolean.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     fallback: Value returned when the key is missing or invalid.
    ///
    /// Returns:
    ///     O booleano lido, ou o valor de recuo.
    /// </summary>
    public bool GetBoolean(string section, string key, bool fallback) =>
        bool.TryParse(GetString(section, key, string.Empty), out bool value) ? value : fallback;

    /// <summary>
    /// Reads a key as an integer.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     fallback: Value returned when the key is missing or invalid.
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
    /// Reads a key as a floating-point number.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     fallback: Value returned when the key is missing or invalid.
    ///
    /// Returns:
    ///     The parsed number, or the fallback value.
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
    /// Reads a key as a pipe-separated list.
    ///
    /// Args:
    ///     section: Section name.
    ///     key: Key name.
    ///     fallback: List returned when the key is missing or empty.
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
    ///     header: Optional comment inserted at the top of the file.
    ///
    /// Returns:
    ///     The text ready to write.
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

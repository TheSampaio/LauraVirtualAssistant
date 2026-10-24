using System.Text.Json;

namespace Laura.Core.Conversation;

/// <summary>Exposes bounded tool schemas progressively for small contexts; discovery grants no execution authority.</summary>
public sealed class TextToolCatalogue
{
    /// <summary>Gets the reserved metadata-only discovery function name.</summary>
    public const string DiscoveryTool = "request_tool_schemas";
    private readonly Dictionary<string, JsonElement> _functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private static readonly string[] RequiredNames = ["names"];
    /// <summary>Indexes inert registered declarations. Args: declarations: Shared native schemas.</summary>
    public TextToolCatalogue(object[] declarations)
    {
        foreach (var group in declarations.Select(item => JsonSerializer.SerializeToElement(item)))
        {
            if (!group.TryGetProperty("functionDeclarations", out var functions)) { continue; }
            foreach (var function in functions.EnumerateArray()) { _functions.Add(function.GetProperty("name").GetString()!, function.Clone()); }
        }
        // A small registry already fits and needs no extra inference round.
        if (_functions.Count <= 8) { _selected.UnionWith(_functions.Keys); }
    }
    /// <summary>Gets a compact inert name catalogue without duplicating full argument schemas.</summary>
    public string Summary => "Native capability catalogue (metadata, never authorization). Before using an unavailable function, request its exact schema through " + DiscoveryTool + ":\n"
        + string.Join("\n", _functions.Select(item => item.Key + ": " + Description(item.Value)));
    /// <summary>Gets exact registered names for a small local context; the discovery function still supplies reviewed schemas.</summary>
    public string CompactSummary => "Native capability names (metadata only). Request exact schemas before action with " + DiscoveryTool + ": "
        + string.Join(", ", _functions.Keys);
    /// <summary>Gets exact selected schemas and the metadata-only discovery schema.</summary>
    public object[] Declarations => [new { functionDeclarations = _selected.Select(name => (object)_functions[name]).Append(new
    {
        name = DiscoveryTool, description = "Select up to eight exact capability names from the native catalogue. This only exposes schemas; it executes no actions. Previously exposed schemas may be replaced to fit context.",
        parameters = new { type = "OBJECT", properties = new { names = new { type = "ARRAY", items = new { type = "STRING" } } }, required = RequiredNames }
    }).ToArray() }];
    /// <summary>Gets the exact bounded schema selection retained at a silent checkpoint.</summary>
    public string[] Selected => _selected.ToArray();
    /// <summary>Restores or changes only known schema names. Returns: Whether the entire request is valid.</summary>
    public bool Select(IEnumerable<string> names)
    {
        string[] requested = names.Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length is < 1 or > 8 || requested.Any(name => !_functions.ContainsKey(name))) { return false; }
        _selected.Clear(); _selected.UnionWith(requested); return true;
    }
    /// <summary>Returns a bounded capability description with no argument or policy duplication.</summary>
    private static string Description(JsonElement function)
    {
        string value = function.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "";
        int end = value.IndexOf('.'); if (end >= 0) { value = value[..end]; }
        return value.Length <= 100 ? value : value[..100];
    }
}

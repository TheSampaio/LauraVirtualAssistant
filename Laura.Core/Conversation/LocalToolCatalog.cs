using System.Text.Json;
using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Projects registered native declarations into bounded discovery metadata and exact on-demand schemas.</summary>
public sealed class LocalToolCatalog
{
    private readonly Dictionary<string, JsonElement> _functions = new(StringComparer.Ordinal);
    /// <summary>Builds a turn-local catalogue. Args: tools: Native implemented declarations, never model-supplied metadata. Raises: InvalidOperationException for ambiguous or excessive declarations.</summary>
    public LocalToolCatalog(IAssistantTools tools)
    {
        foreach (object declaration in tools.Declarations)
        {
            var group = JsonSerializer.SerializeToElement(declaration);
            if (!group.TryGetProperty("functionDeclarations", out var functions)) { continue; }
            foreach (var function in functions.EnumerateArray())
            {
                string name = function.GetProperty("name").GetString()!;
                if (!tools.IsRegisteredTool(name) || name is "screen_process" or "read_brain_image") { continue; }
                if (name == "describe_tool" || _functions.Count >= 128 || !_functions.TryAdd(name, function.Clone()))
                { throw new InvalidOperationException("Ambiguous or excessive local tool declarations."); }
            }
        }
    }

    /// <summary>Gets inert names and bounded description previews; complete schemas are retrieved by exact name before dispatch.</summary>
    public string Discovery => JsonSerializer.Serialize(_functions.Select(pair => new { name = pair.Key, description = Preview(pair.Value.GetProperty("description").GetString() ?? "") }));

    /// <summary>Returns an actual complete native declaration, or null for an unknown or unsupported capability.</summary>
    public JsonElement? Find(string name) => _functions.TryGetValue(name, out var function) ? function : null;
    /// <summary>Gets the exact registered discovery names for constrained metadata lookup.</summary>
    public IReadOnlyList<string> Names => _functions.Keys.ToArray();

    /// <summary>Bounds discovery-only metadata without truncating observations, source passages or executable arguments.</summary>
    private static string Preview(string description) => description.Length <= 96 ? description : description[..96];
}

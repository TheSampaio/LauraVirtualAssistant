using System.Text.Json;
using System.Text.Json.Nodes;

namespace Laura.Core.Brain;

/// <summary>Merges native Obsidian graph groups without CSS, plugins or executable metadata.</summary>
public static class BrainObsidianGroups
{
    /// <summary>Returns graph settings with creator groups, retaining unrelated groups and settings. Raises: IOException when existing creator colors conflict.</summary>
    public static string Merge(string content, bool updateExisting = false)
    {
        JsonObject root;
        try { root = content.Length == 0 ? new() : JsonNode.Parse(content) as JsonObject ?? throw new IOException("Obsidian graph settings must be an object."); }
        catch (JsonException) { throw new IOException("Obsidian graph settings are invalid."); }
        JsonArray groups = root["colorGroups"] as JsonArray ?? new();
        if (root["colorGroups"] is not null && root["colorGroups"] is not JsonArray) { throw new IOException("Obsidian graph groups must be a list."); }
        if (groups.Any(group => group is not JsonObject)) { throw new IOException("Obsidian graph groups must be objects."); }
        foreach (string agent in new[] { "laura", "codex", "claude" })
        {
            string query = "tag:#origin/" + agent;
            int rgb = Convert.ToInt32(BrainProvenance.Color(agent)![1..], 16);
            JsonNode[] matches = groups.Where(group => group?["query"]?.ToString() == query).Select(group => group!).ToArray();
            if (updateExisting)
            { foreach (JsonNode match in matches) { match["color"] = new JsonObject { ["a"] = 1, ["rgb"] = rgb }; } }
            else if (matches.Any(group => group["color"] is not JsonObject || group["color"]?["rgb"]?.ToString() != rgb.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || group["color"]?["a"]?.ToString() != "1")) { throw new IOException("Existing Obsidian creator group conflicts with the requested color. Review graph groups explicitly."); }
            if (matches.Length == 0) { groups.Add(new JsonObject { ["query"] = query, ["color"] = new JsonObject { ["a"] = 1, ["rgb"] = rgb } }); }
        }
        if (root["colorGroups"] is null) { root["colorGroups"] = groups; }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

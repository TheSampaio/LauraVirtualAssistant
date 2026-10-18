using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Laura.Core.Brain;

/// <summary>Versioned Markdown/YAML codec preserving unrelated metadata and conventional human-readable notes.</summary>
public static partial class BrainMarkdown
{
    /// <summary>Gets the relation vocabulary offered by native authoring tools; external relation identifiers remain inert data.</summary>
    public static IReadOnlySet<string> RelationTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "related_to", "belongs_to", "depends_on", "uses", "prefers", "contradicts", "supports", "derived_from", "developed_by", "part_of", "learned_from", "replaces" };
    /// <summary>Reads known fields with bounds while retaining the complete original YAML and body.</summary>
    public static BrainNote Parse(string content, string path)
    {
        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        string yaml = "", body = normalized;
        if (normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            int end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end < 0 || (end + 4 < normalized.Length && normalized[end + 4] != '\n')) { throw new FormatException("Invalid frontmatter boundary."); }
            yaml = normalized[4..end]; body = normalized[(end + 4)..].TrimStart('\n');
        }
        YamlMappingNode map = Mapping(yaml);
        if (Scalar(map, "schema_version", "1") != "1") { throw new FormatException("Unsupported Brain schema version."); }
        string id = Scalar(map, "id", "");
        if (id.Length > 100 || (id.Length > 0 && !Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))) { throw new FormatException("Invalid stable identity."); }
        var relations = new List<BrainRelation>();
        if (Node(map, "relations") is YamlSequenceNode sequence)
        {
            foreach (YamlMappingNode relation in sequence.Children.OfType<YamlMappingNode>().Take(200))
            {
                string type = Scalar(relation, "type", "related_to"), target = Scalar(relation, "target", "");
                if (!RelationTypePattern().IsMatch(type) || target.Length is 0 or > 100) { throw new FormatException("Invalid relation."); }
                relations.Add(new(target, type, Number(relation, "weight", .5), Number(relation, "confidence", .7), Scalar(relation, "evidence", ""), Scalar(relation, "source", "manual")));
            }
        }
        return new()
        {
            Id = id, Path = path.Replace('\\', '/'), Title = Scalar(map, "title", Scalar(map, "name", System.IO.Path.GetFileNameWithoutExtension(path))),
            Description = Scalar(map, "description", ""), OriginAgent = Scalar(map, "origin_agent", ""), LastUpdatedBy = Scalar(map, "last_updated_by", ""),
            Procedure = Node(map, "laura_skill") is YamlMappingNode procedure ? BrainSkillCodec.Read(procedure) : null,
            Type = Scalar(map, "type", path.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) ? "skill" : "document"),
            MemoryType = Scalar(map, "memory_type", "semantic"), Status = Scalar(map, "status", "active"),
            CreatedAt = Date(map, "created_at"), UpdatedAt = Date(map, "updated_at"), Confidence = Number(map, "confidence", .8), Importance = Number(map, "importance", .5),
            Tags = Tags(map), Aliases = Strings(map, "aliases"), Source = Scalar(map, "source", "manual"), Verification = Scalar(map, "verification", "unspecified"), Key = Scalar(map, "key", ""),
            Revision = Scalar(map, "revision", ""), PreviousRevision = Scalar(map, "previous_revision", ""), Relations = relations,
            Application = Node(map, "application") is YamlMappingNode application && Scalar(application, "target", "").Length <= 4096
                && (Scalar(application, "sha256", "").Length == 64 || Scalar(application, "sha256", "").Length == 0
                    && Scalar(map, "source", "") == "user_application_declaration" && Scalar(map, "verification", "") is "user_reported" or "unverified")
                ? new(Scalar(map, "title", ""), Scalar(application, "target", ""), Scalar(application, "sha256", "")) : null,
            Body = body, Frontmatter = yaml, Hash = FileBrainStorage.Hash(content)
        };
    }
    /// <summary>Updates known metadata without dropping external skill fields and writes conventional YAML lists.</summary>
    public static string Render(BrainNote note)
    {
        YamlMappingNode map = Mapping(note.Frontmatter);
        foreach ((string key, string value) in new[] { ("id", note.Id), ("schema_version", "1"), ("title", note.Title), ("description", note.Description), ("type", note.Type),
            ("memory_type", note.MemoryType), ("created_at", note.CreatedAt.ToString("O", CultureInfo.InvariantCulture)), ("updated_at", note.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("status", note.Status), ("confidence", note.Confidence.ToString(CultureInfo.InvariantCulture)), ("importance", note.Importance.ToString(CultureInfo.InvariantCulture)),
            ("source", note.Source), ("verification", note.Verification), ("key", note.Key), ("revision", note.Revision), ("previous_revision", note.PreviousRevision) })
        { map.Children[new YamlScalarNode(key)] = new YamlScalarNode(value); }
        if (note.Type == "skill" && !map.Children.ContainsKey(new YamlScalarNode("name")))
        { map.Children[new YamlScalarNode("name")] = new YamlScalarNode(note.Title); }
        if (note.OriginAgent.Length > 0) { map.Children[new YamlScalarNode("origin_agent")] = new YamlScalarNode(note.OriginAgent); }
        if (note.LastUpdatedBy.Length > 0) { map.Children[new YamlScalarNode("last_updated_by")] = new YamlScalarNode(note.LastUpdatedBy); }
        map.Children[new YamlScalarNode("tags")] = new YamlSequenceNode(Tags(map).Concat(BrainProvenance.Tags(note)).Distinct(StringComparer.Ordinal).Select(tag => new YamlScalarNode(tag)));
        map.Children[new YamlScalarNode("aliases")] = new YamlSequenceNode(note.Aliases.Select(alias => new YamlScalarNode(alias)));
        map.Children[new YamlScalarNode("relations")] = new YamlSequenceNode(note.Relations.Select(relation => new YamlMappingNode
        {
            { "target", relation.Target }, { "type", relation.Type }, { "weight", relation.Weight.ToString(CultureInfo.InvariantCulture) },
            { "confidence", relation.Confidence.ToString(CultureInfo.InvariantCulture) }, { "evidence", relation.Evidence }, { "source", relation.Source }
        }));
        if (note.Application is { } application) { map.Children[new YamlScalarNode("application")] = new YamlMappingNode { { "target", application.Target }, { "sha256", application.Hash } }; }
        if (note.Procedure is { } procedure) { map.Children[new YamlScalarNode("laura_skill")] = BrainSkillCodec.Write(procedure); }
        using var writer = new StringWriter(CultureInfo.InvariantCulture); new YamlStream(new YamlDocument(map)).Save(writer, false);
        string yaml = writer.ToString().Trim().TrimEnd('.').TrimEnd();
        return "---\n" + yaml + "\n---\n\n" + note.Body.TrimEnd() + "\n";
    }
    /// <summary>Merges supplied metadata into existing YAML while keeping creator provenance immutable.</summary>
    public static string MergeFrontmatter(string previous, string incoming)
    {
        YamlMappingNode map = Mapping(previous);
        foreach (var pair in Mapping(incoming).Children)
        { if (pair.Key is YamlScalarNode key && key.Value != "origin_agent") { map.Children[pair.Key] = pair.Value; } }
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(map)).Save(writer, false);
        return writer.ToString().Trim().TrimEnd('.').TrimEnd();
    }
    /// <summary>Extracts wikilink destinations separately from human-facing display aliases and headings.</summary>
    public static IReadOnlyList<string> Links(string body) => LinkPattern().Matches(body).Select(match => match.Groups[1].Value.Split('|')[0].Split('#')[0].Trim()).Where(link => link.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    /// <summary>Reads YAML as data only and rejects duplicate keys through the representation parser.</summary>
    private static YamlMappingNode Mapping(string yaml)
    {
        if (yaml.Length == 0) { return new(); }
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlDotNet.Core.YamlException) { throw new FormatException("Invalid frontmatter YAML."); }
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode map) { throw new FormatException("Frontmatter must be one YAML mapping."); }
        var pending = new Stack<(YamlNode Node, int Depth)>(); pending.Push((map, 0)); var visited = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            if (item.Depth > 24 || visited.Count > 20000 || !visited.Add(item.Node)) { throw new FormatException("YAML aliases or excessively nested metadata are unsupported."); }
            if (item.Node is YamlMappingNode mapping) { foreach (var pair in mapping.Children) { if (pair.Key is not YamlScalarNode) { throw new FormatException("Metadata keys must be text."); } pending.Push((pair.Value, item.Depth + 1)); } }
            else if (item.Node is YamlSequenceNode sequence) { foreach (YamlNode child in sequence.Children) { pending.Push((child, item.Depth + 1)); } }
        }
        return map;
    }
    /// <summary>Resolves a field without interpreting tags or arbitrary object types.</summary>
    private static YamlNode? Node(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node) ? node : null;
    /// <summary>Reads a scalar with a conservative fallback.</summary>
    private static string Scalar(YamlMappingNode map, string key, string fallback) => Node(map, key) is YamlScalarNode node ? node.Value ?? fallback : fallback;
    /// <summary>Reads bounded aliases without changing existing indexing limits.</summary>
    private static string[] Strings(YamlMappingNode map, string key) => Node(map, key) is YamlSequenceNode sequence ? sequence.Children.OfType<YamlScalarNode>().Select(node => node.Value ?? "").Where(value => value.Length is > 0 and < 250).Take(100).ToArray() : [];
    /// <summary>Preserves all authored tag strings within the document size limit and rejects shapes that cannot be merged losslessly.</summary>
    private static string[] Tags(YamlMappingNode map) => Node(map, "tags") switch
    {
        null => [],
        YamlScalarNode scalar => string.IsNullOrWhiteSpace(scalar.Value) ? [] : [scalar.Value],
        YamlSequenceNode sequence when sequence.Children.All(child => child is YamlScalarNode) => sequence.Children.Cast<YamlScalarNode>().Select(child => child.Value ?? "").ToArray(),
        _ => throw new FormatException("Brain tags must be text or a list of text.")
    };
    /// <summary>Reads authored timestamps without assigning future certainty to missing dates.</summary>
    private static DateTimeOffset Date(YamlMappingNode map, string key) => DateTimeOffset.TryParse(Scalar(map, key, ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : DateTimeOffset.UtcNow;
    /// <summary>Bounds independent confidence and importance scores.</summary>
    private static double Number(YamlMappingNode map, string key, double fallback) => double.TryParse(Scalar(map, key, ""), CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    [GeneratedRegex(@"\[\[([^\]\r\n]+)\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();
    [GeneratedRegex(@"\A[a-zA-Z][a-zA-Z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RelationTypePattern();
}

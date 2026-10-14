using YamlDotNet.RepresentationModel;

namespace Laura.Core.Brain;

/// <summary>Exact authored evidence or component version; these references grant no execution authority.</summary>
public sealed record BrainSkillReference(string Id, string Hash, string Evidence = "");

/// <summary>Portable, declarative procedure metadata. Dependencies describe requirements, never installation instructions to execute.</summary>
public sealed record BrainSkillDefinition(string Kind, string Purpose, IReadOnlyList<string> UseWhen,
    IReadOnlyList<string> Prerequisites, IReadOnlyList<string> Dependencies, IReadOnlyList<string> Tools,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> Steps, IReadOnlyList<string> Limitations,
    IReadOnlyList<string> SuccessCriteria, IReadOnlyList<string> FailureBehavior, IReadOnlyList<string> Capabilities,
    IReadOnlyList<BrainSkillReference> Sources, IReadOnlyList<BrainSkillReference> Components);

/// <summary>A model-authored draft or exact-version revision request, never an approval or verified outcome.</summary>
public sealed record BrainSkillProposal(string Name, string Description, BrainSkillDefinition Definition, string TargetId = "", string TargetHash = "");

/// <summary>Separates exact local approval from actual instruction/dependency readiness.</summary>
public sealed record BrainSkillAvailability(bool Approved, bool Ready, string State, IReadOnlyList<string> Missing);

internal static class BrainSkillCodec
{
    internal static BrainSkillDefinition Read(YamlMappingNode map) => new(Text(map, "kind"), Text(map, "purpose"), List(map, "use_when"),
        List(map, "prerequisites"), List(map, "dependencies"), List(map, "tools"), List(map, "permissions"), List(map, "steps"),
        List(map, "limitations"), List(map, "success_criteria"), List(map, "failure_behavior"), List(map, "capabilities"), References(map, "sources"), References(map, "components"));
    internal static YamlMappingNode Write(BrainSkillDefinition definition) => new()
    {
        { "kind", definition.Kind }, { "purpose", definition.Purpose },
        { "use_when", Sequence(definition.UseWhen) }, { "prerequisites", Sequence(definition.Prerequisites) },
        { "dependencies", Sequence(definition.Dependencies) }, { "tools", Sequence(definition.Tools) },
        { "permissions", Sequence(definition.Permissions) }, { "steps", Sequence(definition.Steps) },
        { "limitations", Sequence(definition.Limitations) }, { "success_criteria", Sequence(definition.SuccessCriteria) },
        { "failure_behavior", Sequence(definition.FailureBehavior) }, { "capabilities", Sequence(definition.Capabilities) },
        { "sources", ReferenceSequence(definition.Sources) }, { "components", ReferenceSequence(definition.Components) }
    };
    private static YamlSequenceNode Sequence(IReadOnlyList<string> values) => new(values.Select(value => new YamlScalarNode(value)));
    private static YamlSequenceNode ReferenceSequence(IReadOnlyList<BrainSkillReference> values) => new(values.Select(value => new YamlMappingNode
        { { "id", value.Id }, { "hash", value.Hash }, { "evidence", value.Evidence } }));
    private static string Text(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out var value)
        && value is YamlScalarNode text && text.Value is { Length: <= 2000 } ? text.Value : throw new FormatException("Invalid procedural metadata text.");
    private static string[] List(YamlMappingNode map, string key)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is not YamlSequenceNode list || list.Children.Count > 32)
        { throw new FormatException("Invalid procedural metadata list."); }
        return list.Children.Select(item => item is YamlScalarNode text && text.Value is { Length: > 0 and <= 2000 } ? text.Value
            : throw new FormatException("Invalid procedural metadata item.")).ToArray();
    }
    private static BrainSkillReference[] References(YamlMappingNode map, string key)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is not YamlSequenceNode list || list.Children.Count > 8)
        { throw new FormatException("Invalid procedural references."); }
        return list.Children.Select(item => item is YamlMappingNode reference ? new BrainSkillReference(Text(reference, "id"), Text(reference, "hash"), Text(reference, "evidence"))
            : throw new FormatException("Invalid procedural reference.")).ToArray();
    }
}

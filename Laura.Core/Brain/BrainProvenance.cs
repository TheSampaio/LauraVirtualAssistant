namespace Laura.Core.Brain;

/// <summary>Maps inert creator metadata to portable graph styling without inferring legacy origins.</summary>
public static class BrainProvenance
{
    /// <summary>Returns the creator color, or null to retain the theme default.</summary>
    public static string? Color(string origin) => origin switch
    {
        "laura" => "#D83A49", "codex" => "#3B82F6", "claude" => "#F59E0B", _ => null
    };

    /// <summary>Returns creator fill and a separate selection outline so selection never hides provenance.</summary>
    public static BrainNodeAppearance Appearance(string origin, bool selected) => new(Color(origin), selected ? 3 : 1, selected ? 3 : 0);

    /// <summary>Reports contradictory origin tags without rewriting authored provenance.</summary>
    public static bool HasConflict(BrainNote note) => note.Tags.Any(tag => tag.StartsWith("origin/", StringComparison.Ordinal) && tag != "origin/" + note.OriginAgent);

    /// <summary>Merges the creator tag without discarding authored tags or interpreting them as authority.</summary>
    public static IReadOnlyList<string> Tags(BrainNote note) => Color(note.OriginAgent) is null ? note.Tags
        : note.Tags.Concat(["origin/" + note.OriginAgent]).Distinct(StringComparer.Ordinal).ToArray();
}

/// <summary>UI-independent graph appearance with theme fallback and a non-color selection cue.</summary>
public sealed record BrainNodeAppearance(string? Fill, float OutlineWidth, float OutlineOffset);

namespace Laura.Core.Engine;

/// <summary>Explicit user-authored bounded command sequences; no model text is automatically executed.</summary>
public static class CommandSequence
{
    /// <summary>Gets the maximum number of skills that one sequence may execute.</summary>
    public const int MaximumSteps = 4;
    /// <summary>Parses an explicit localized sequence prefix and rejects nested or empty steps.</summary>
    public static IReadOnlyList<string>? Parse(string text, IReadOnlyList<string> prefixes)
    {
        string? prefix = prefixes.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (prefix is null) { return null; }
        string[] steps = text[prefix.Length..].Split(';', StringSplitOptions.TrimEntries);
        return steps.Length <= MaximumSteps && steps.Length > 0
            && steps.All(step => step.Length > 0 && !prefixes.Any(candidate => step.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                ? steps : [];
    }
}

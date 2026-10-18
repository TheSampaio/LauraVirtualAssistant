namespace Laura.Core.Brain;

/// <summary>Closed proposal-rejection categories; values disclose no source text, YAML or private model input.</summary>
public enum BrainSkillContract
{
    /// <summary>Name, description, purpose or kind violates the bounded identity contract.</summary>
    Identity,
    /// <summary>A required structured array is missing, too large or contains an invalid value.</summary>
    StructuredLists,
    /// <summary>A guidance-only proposal incorrectly claims external dependencies, tools or composition.</summary>
    GuidanceKind,
    /// <summary>Required procedure/evidence/outcome fields or component identities are incomplete.</summary>
    EvidenceAndOutcomes,
    /// <summary>A cited knowledge version or literal quote is absent from current source evidence.</summary>
    CurrentLiteralSource
}

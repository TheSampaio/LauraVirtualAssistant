namespace Laura.Core.Brain;

/// <summary>A stable, portable Markdown node with preserved frontmatter and optimistic edit identity.</summary>
public sealed record BrainNote
{
    /// <summary>Gets the permanent node identity.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("D");
    /// <summary>Gets the portable path relative to the vault.</summary>
    public string Path { get; init; } = "";
    /// <summary>Gets the user-readable title.</summary>
    public string Title { get; init; } = "";
    /// <summary>Gets imported task guidance metadata for discovery without loading full skill instructions.</summary>
    public string Description { get; init; } = "";
    /// <summary>Gets the entity category.</summary>
    public string Type { get; init; } = "document";
    /// <summary>Gets semantic, episodic or procedural memory classification.</summary>
    public string MemoryType { get; init; } = "semantic";
    /// <summary>Gets the creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Gets the last authored update time.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Gets active, archived, conflict or draft state.</summary>
    public string Status { get; init; } = "active";
    /// <summary>Gets evidence confidence independently of recurrence.</summary>
    public double Confidence { get; init; } = .8;
    /// <summary>Gets user importance independently of confidence.</summary>
    public double Importance { get; init; } = .5;
    /// <summary>Gets immutable creator provenance, independent of ownership and execution permission.</summary>
    public string OriginAgent { get; init; } = "";
    /// <summary>Gets the explicitly recorded latest editor; absent legacy metadata remains unattributed.</summary>
    public string LastUpdatedBy { get; init; } = "";
    /// <summary>Gets portable categories.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Gets former names and paths preserved during moves.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
    /// <summary>Gets the attributed source, never an execution permission.</summary>
    public string Source { get; init; } = "manual";
    /// <summary>Gets evidence status separately from confidence: unspecified, user_reported, unverified, observed_execution or verified. Authored labels never grant capabilities.</summary>
    public string Verification { get; init; } = "unspecified";
    /// <summary>Gets an inert local executable hint. It never authorizes execution and is excluded from automatic prompt content.</summary>
    public BrainApplicationReference? Application { get; init; }
    /// <summary>Gets inert structured procedure requirements and evidence, independent of local approval.</summary>
    public BrainSkillDefinition? Procedure { get; init; }
    /// <summary>Gets a structured consolidation key, if applicable.</summary>
    public string Key { get; init; } = "";
    /// <summary>Gets the authored revision identity.</summary>
    public string Revision { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets the parent revision for conflict inspection.</summary>
    public string PreviousRevision { get; init; } = "";
    /// <summary>Gets factual, attributed relations.</summary>
    public IReadOnlyList<BrainRelation> Relations { get; init; } = [];
    /// <summary>Gets conventional Markdown body text.</summary>
    public string Body { get; init; } = "";
    /// <summary>Gets the original YAML, preserving unrelated editor/skill metadata on writes.</summary>
    public string Frontmatter { get; init; } = "";
    /// <summary>Gets the full file hash used for optimistic concurrency.</summary>
    public string Hash { get; init; } = "";
    /// <summary>Gets whether retrieval may use the node as knowledge.</summary>
    public bool IsKnowledge => Status == "active" && !Path.StartsWith("archive/", StringComparison.OrdinalIgnoreCase) && Type != "skill" && Path != "README.md";
}

/// <summary>An attributed, directed relation; similarity alone never creates one.</summary>
public sealed record BrainRelation(string Target, string Type = "related_to", double Weight = .5, double Confidence = .7, string Evidence = "", string Source = "manual");
/// <summary>A resolved link with provenance and direction.</summary>
public sealed record BrainEdge(string Source, string Target, string Type, double Weight, double Confidence, string Evidence, string Provenance = "manual");
/// <summary>A non-sensitive integrity diagnostic tied to a portable path.</summary>
public sealed record BrainIssue(string Path, string Code);
/// <summary>An immutable view published after a coherent scan.</summary>
public sealed record BrainSnapshot(IReadOnlyList<BrainNote> Notes, IReadOnlyList<BrainEdge> Edges, IReadOnlyList<BrainIssue> Issues)
{
    /// <summary>Gets the initial empty view.</summary>
    public static BrainSnapshot Empty { get; } = new([], [], []);
}
/// <summary>A ranked memory with a bounded excerpt and independently inspectable retrieval scores.</summary>
public sealed record BrainHit(BrainNote Note, double Score, double Lexical, double Semantic, double Graph, string Excerpt)
{
    /// <summary>Gets the derived matching passage; graph-only hits may have no direct passage.</summary>
    public BrainPassage? Passage { get; init; }
    /// <summary>Gets the actual source offset of the excerpt for progressive expansion.</summary>
    public int ExcerptStart { get; init; }
    /// <summary>Gets an attributed authored path when retrieval reached this note through explicit relations.</summary>
    public BrainActivation? Activation { get; init; }
}
/// <summary>An optimistic-write conflict whose draft was preserved without overwriting current knowledge.</summary>
public sealed class BrainConflictException : IOException
{
    /// <summary>Creates a privacy-safe failure without embedding the note body in the exception.</summary>
    public BrainConflictException() : base("The note changed externally. A conflicting draft was preserved in the vault.") { }
}

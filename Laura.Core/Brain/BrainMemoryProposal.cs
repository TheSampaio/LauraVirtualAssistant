namespace Laura.Core.Brain;

/// <summary>An interpretation proposed by a model; all content must be grounded in the current user declaration.</summary>
public sealed record BrainMemoryProposal(string Category, string Aspect, string Value, string Evidence, string Change = "new", string TargetId = "");

/// <summary>A knowledge-only consolidation result; it grants no execution capability.</summary>
public sealed record BrainMemoryResult(string Disposition, string NoteId = "", string Reason = "");

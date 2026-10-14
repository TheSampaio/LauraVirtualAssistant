using System.Text.Json;

namespace Laura.Core.Abstractions;

/// <summary>Publishes structured, implemented computer capabilities to the live model.</summary>
public interface IAssistantTools
{
    /// <summary>Gets the public function schemas for available adapters.</summary>
    object[] Declarations { get; }
    /// <summary>Executes one structured action with native confirmation where required.</summary>
    Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>Returns observable tool output and an optional explicitly captured vision frame.</summary>
public sealed record AssistantToolResult(string Text, byte[]? Image = null, string MimeType = "image/jpeg")
{
    /// <summary>Cancels future disclosure if the originating editable capture is closed.</summary>
    public CancellationToken ImageLifetime { get; init; }
    /// <summary>Gets native completed-process evidence, independent of generated text or presumed task success.</summary>
    public Laura.Core.Brain.BrainExecutionObservation? Observation { get; init; }
    /// <summary>Gets the separate outcome of optional knowledge consolidation; it never changes the execution result.</summary>
    public Laura.Core.Brain.BrainMemoryResult? Learning { get; init; }
    /// <summary>Gets a native adapter failure/refusal indicator; successful authorization alone is not execution success.</summary>
    public bool Failed { get; init; }
}

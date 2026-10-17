using System.Text.Json;

namespace Laura.Core.Abstractions;

/// <summary>Publishes structured, implemented computer capabilities to the live model.</summary>
public interface IAssistantTools
{
    /// <summary>Gets the public function schemas for available adapters.</summary>
    object[] Declarations { get; }
    /// <summary>Gets the closed registry used to validate task plans; schemas remain inert metadata.</summary>
    IReadOnlySet<string> RegisteredToolNames => Declarations.SelectMany(declaration =>
    {
        var group = JsonSerializer.SerializeToElement(declaration);
        return group.TryGetProperty("functionDeclarations", out var functions) ? functions.EnumerateArray().Select(item => item.Clone())
            : group.TryGetProperty("name", out _) ? new[] { group } : Enumerable.Empty<JsonElement>();
    }).Select(function => function.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
    /// <summary>Identifies fixed registered names for payload-free diagnostics; unknown remote strings must not become log labels.</summary>
    bool IsRegisteredTool(string name) => false;
    /// <summary>Executes one structured action with native confirmation where required.</summary>
    Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
    /// <summary>Returns bounded native pending work for the same user objective after model turn completion, or null when no continuation is justified.</summary>
    Task<string?> PendingTaskContinuationAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
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
    /// <summary>Gets actual native launch evidence; shell acceptance without a process ID remains unverified.</summary>
    public ProcessLaunchResult? Launch { get; init; }
    /// <summary>Gets a native background-job identity observed at creation, never parsed from generated text.</summary>
    public Guid? CommandJobId { get; init; }
    /// <summary>Gets the adapter-confirmed outcome: success, failed, cancelled or blocked; never supplied by the model.</summary>
    public string Status { get; init; } = "success";
    /// <summary>Gets native evidence-based remaining work within the original request; it never authorizes another effect.</summary>
    public string? PendingWork { get; init; }
}

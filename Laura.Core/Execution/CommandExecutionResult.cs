namespace Laura.Core.Execution;

/// <summary>Observed process result; starting or authorizing a process is not proof of task completion.</summary>
public sealed record CommandExecutionResult(int? ProcessId, int? ExitCode, string StandardOutput, string StandardError,
    string Status, bool Truncated, TimeSpan Duration)
{
    /// <summary>Gets the native invocation identity for correlation with authorization and process events.</summary>
    public Guid OperationId { get; init; }
    /// <summary>Gets the resolved executable used by the native adapter.</summary>
    public string Executable { get; init; } = "";
    /// <summary>Gets the reviewed working directory.</summary>
    public string WorkingDirectory { get; init; } = "";
    /// <summary>Gets the UTC timestamp immediately before native creation.</summary>
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>Gets the observed terminal timestamp, including cleanup.</summary>
    public DateTimeOffset CompletedAt { get; init; }
    /// <summary>Gets descriptive native authorization metadata, never an approval token.</summary>
    public string Authorization { get; init; } = "";
    /// <summary>Gets whether the process itself exited successfully; the user's goal still needs separate verification.</summary>
    public bool Succeeded => Status == "exited" && ExitCode == 0;
}

/// <summary>A bounded ordered output chunk. Sequence identifies delivery order, not a claimed global OS stream ordering.</summary>
public sealed record CommandOutput(long Sequence, bool StandardError, string Text);

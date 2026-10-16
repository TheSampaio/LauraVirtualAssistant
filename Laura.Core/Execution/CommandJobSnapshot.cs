namespace Laura.Core.Execution;

/// <summary>Bounded in-memory process observation. Job identity is native; a PID is diagnostic data, never authority.</summary>
public sealed record CommandJobSnapshot(Guid Id, Guid TaskId, int? ProcessId, string Status, int? ExitCode,
    DateTimeOffset Created, TimeSpan Duration, long LatestSequence, long NextSequence, bool OutputLost,
    bool Truncated, IReadOnlyList<CommandOutput> Output)
{
    /// <summary>Gets whether no further execution or stream observation is expected.</summary>
    public bool Finished => Status is not ("awaiting_approval" or "running");
    /// <summary>Gets whether the actual initial process exited successfully, without claiming goal completion.</summary>
    public bool Succeeded => Status == "exited" && ExitCode == 0;
}

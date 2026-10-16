namespace Laura.Core.Execution;

/// <summary>Observed process result; starting or authorizing a process is not proof of task completion.</summary>
public sealed record CommandExecutionResult(int? ProcessId, int? ExitCode, string StandardOutput, string StandardError,
    string Status, bool Truncated, TimeSpan Duration)
{
    /// <summary>Gets whether the process itself exited successfully; the user's goal still needs separate verification.</summary>
    public bool Succeeded => Status == "exited" && ExitCode == 0;
}

/// <summary>A bounded ordered output chunk. Sequence identifies delivery order, not a claimed global OS stream ordering.</summary>
public sealed record CommandOutput(long Sequence, bool StandardError, string Text);

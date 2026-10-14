namespace Laura.Core.Abstractions;

/// <summary>Distinguishes shell acceptance from actual process creation. It does not assert window readiness or successful task completion.</summary>
public sealed record ProcessLaunchResult(bool Accepted, string Target, int? ProcessId = null, string TargetHash = "", string Reason = "")
{
    /// <summary>Prevents retargeting after an explicit denial or failure to validate a pinned reference.</summary>
    public bool StopFallback { get; init; }
}

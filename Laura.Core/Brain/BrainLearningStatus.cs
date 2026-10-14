namespace Laura.Core.Brain;

/// <summary>Reports whether an automatic-learning event was actually accepted.</summary>
public enum BrainObservationResult
{
    /// <summary>The bounded worker accepted the event.</summary>
    Accepted,
    /// <summary>Privacy, size or user settings exclude the event.</summary>
    Excluded,
    /// <summary>The caller must retry or use the waiting asynchronous entry point.</summary>
    Saturated,
    /// <summary>Shutdown has begun; no new work was accepted.</summary>
    Stopped
}

/// <summary>Privacy-safe lifetime counters; counts never include message content.</summary>
public sealed record BrainLearningStatus(long Accepted, long Completed, long Failed, long Excluded, long Saturated)
{
    /// <summary>Gets accepted work not yet completed or reported as failed.</summary>
    public long Pending => Math.Max(0, Accepted - Completed - Failed);
}

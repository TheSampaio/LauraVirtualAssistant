namespace Laura.Core.Engine;

/// <summary>Durable task-cycle metadata, independent of individual model timeouts and native effect receipts.</summary>
public sealed record CognitiveRoundProgress(int Cycles = 0, int MaximumCycles = 32, long ActiveMilliseconds = 0, long MaximumActiveMilliseconds = 600000,
    int Recoveries = 0, int MaximumRecoveries = 2, long ResultBytes = 0, long MaximumResultBytes = 16777216,
    string Provider = "", string Model = "", string Outcome = "", bool NeedsContinuation = false, bool Paused = false, bool Canceled = false,
    int ProviderCalls = 0, int MaximumProviderCalls = 64)
{
    /// <summary>Gets whether a new cycle is within cumulative budgets and explicit native task controls.</summary>
    public bool CanContinue => !Paused && !Canceled && Cycles < MaximumCycles && ActiveMilliseconds < MaximumActiveMilliseconds
        && Recoveries <= MaximumRecoveries && ResultBytes < MaximumResultBytes && ProviderCalls < MaximumProviderCalls;
    /// <summary>Rejects corrupt metadata; stored provider names remain descriptive and never confer authorization.</summary>
    public CognitiveRoundProgress Validated()
    {
        if (Cycles is < 0 or > 128 || MaximumCycles is < 1 or > 128 || ActiveMilliseconds < 0 || MaximumActiveMilliseconds is < 1 or > 3600000
            || Recoveries is < 0 or > 9 || MaximumRecoveries is < 0 or > 8 || ResultBytes < 0 || MaximumResultBytes is < 1 or > 67108864
            || ProviderCalls is < 0 or > 256 || MaximumProviderCalls is < 1 or > 256
            || Provider is not ("" or "gemini" or "chatgpt") || Model is null || Model.Length > 256
            || Outcome is not ("" or "running" or "completed_round" or "timeout" or "failed" or "canceled" or "budget"))
        { throw new InvalidDataException("Task cycle checkpoint is invalid."); }
        return this;
    }
}

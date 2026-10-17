namespace Laura.Core.Configuration;

/// <summary>Independent task-cycle, tool, recovery and resource budgets; model deadlines remain in provider settings.</summary>
public sealed record CognitiveTaskOptions
{
    /// <summary>Gets the maximum automatically admitted task cycles.</summary>
    public int MaximumCycles { get; init; } = 32;
    /// <summary>Gets the maximum active task duration, excluding pauses and downtime.</summary>
    public int ActiveMinutes { get; init; } = 10;
    /// <summary>Gets the cumulative native tool-call budget.</summary>
    public int MaximumToolCalls { get; init; } = 128;
    /// <summary>Gets bounded model-timeout recovery attempts; permission denials and uncertain effects never auto-recover.</summary>
    public int MaximumRecoveries { get; init; } = 2;
    /// <summary>Gets the cumulative admitted inference-call budget across providers.</summary>
    public int MaximumProviderCalls { get; init; } = 64;
    /// <summary>Gets the maximum idle interval before another explicit native resume gesture is required.</summary>
    public int IdleMinutes { get; init; } = 1440;
    /// <summary>Gets an independent maximum for a single native tool operation, excluding model wait.</summary>
    public int ToolTimeoutSeconds { get; init; } = 120;
    /// <summary>Gets the maximum accumulated tool-result bytes, including acquired content and metadata.</summary>
    public int MaximumResultMegabytes { get; init; } = 16;
    /// <summary>Sanitizes cumulative limits without changing any owner authorization.</summary>
    public CognitiveTaskOptions Sanitized() => this with
    {
        MaximumCycles = Math.Clamp(MaximumCycles, 1, 128), ActiveMinutes = Math.Clamp(ActiveMinutes, 1, 60), MaximumToolCalls = Math.Clamp(MaximumToolCalls, 1, 256),
        MaximumRecoveries = Math.Clamp(MaximumRecoveries, 0, 8), MaximumResultMegabytes = Math.Clamp(MaximumResultMegabytes, 1, 64),
        MaximumProviderCalls = Math.Clamp(MaximumProviderCalls, 1, 256), IdleMinutes = Math.Clamp(IdleMinutes, 1, 10080), ToolTimeoutSeconds = Math.Clamp(ToolTimeoutSeconds, 5, 600)
    };
}

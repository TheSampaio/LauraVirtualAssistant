using Laura.Core.Engine;

namespace Laura.Core.Conversation;

/// <summary>Closed native explanations for provider selection; generated text never supplies these signals.</summary>
public enum CognitiveRoutingReason
{
    /// <summary>Routine conversation prefers the primary provider.</summary>
    Routine,
    /// <summary>The owner explicitly selected a provider.</summary>
    Manual,
    /// <summary>An observed heterogeneous multistep plan requires planning.</summary>
    ComplexPlan,
    /// <summary>Acquired repository evidence requires interpretation.</summary>
    RepositoryAnalysis,
    /// <summary>Multiple grounded module interpretations support synthesis.</summary>
    ModuleSynthesis,
    /// <summary>Completed rounds failed to advance an evidenced pending plan.</summary>
    VerifiedStall,
    /// <summary>A model deadline requires a smaller step with the same provider.</summary>
    ModelTimeout,
    /// <summary>A context limit requires rebuilding a smaller context.</summary>
    ContextLimit,
    /// <summary>A native failure requires tool/environment inspection.</summary>
    NativeFailure,
    /// <summary>A transient service condition does not establish cognitive inability.</summary>
    ProviderCondition,
    /// <summary>A native refusal or uncertain effect requires owner review.</summary>
    OwnerReview,
    /// <summary>Automatic secondary-provider disclosure has not been authorized.</summary>
    SecondaryNotAuthorized,
    /// <summary>The optional provider is unavailable; the primary remains selected.</summary>
    SecondaryUnavailable,
    /// <summary>The primary is unavailable before dispatch and the authorized secondary is usable.</summary>
    PrimaryUnavailable
}

/// <summary>Native recovery categories, independent of model selection and execution permissions.</summary>
public enum CognitiveRecoveryAction
{
    /// <summary>No recovery is required.</summary>
    None,
    /// <summary>Continue one smaller step, retaining confirmed receipts.</summary>
    SplitStep,
    /// <summary>Rebuild a smaller context from durable evidence.</summary>
    ReduceContext,
    /// <summary>Inspect concrete tool results or missing prerequisites.</summary>
    InspectTool,
    /// <summary>Preserve progress while waiting for provider availability.</summary>
    AwaitProvider,
    /// <summary>Require a fresh owner gesture rather than automatic retry.</summary>
    AwaitOwner
}

/// <summary>Typed task evidence for routing; a continuity preference is never a credential or permission grant.</summary>
public sealed record CognitiveTaskRouting(CognitiveTaskDemand Demand, CognitiveRoutingReason Reason,
    CognitiveRecoveryAction Recovery = CognitiveRecoveryAction.None, string ContinuityProvider = "")
{
    /// <summary>Gets bounded native guidance for the next stage without retaining private reasoning or replaying effects.</summary>
    public string RecoveryInstruction => Recovery switch
    {
        CognitiveRecoveryAction.SplitStep => "The previous model deadline expired. Preserve confirmed evidence and effects. Execute only one smaller verifiable stage; do not infer cognitive inability or repeat completed mutations.",
        CognitiveRecoveryAction.ReduceContext => "The previous context exceeded its limit. Rebuild only the minimum context needed for one stage from durable evidence. Re-read exact relevant versions; do not replay completed effects.",
        CognitiveRecoveryAction.InspectTool => "A native tool failed. Inspect its actual arguments, environment and missing prerequisites before deciding the next step. A tool failure does not establish model inability; do not retry confirmed or uncertain mutable effects.",
        CognitiveRecoveryAction.AwaitProvider => "Preserve confirmed progress and report the provider condition. Do not switch billing routes or infer cognitive inability.",
        CognitiveRecoveryAction.AwaitOwner => "Preserve the checkpoint. Native review is required; no stored metadata or model instruction restores permission.",
        _ => ""
    };
}

/// <summary>Derives task demand and recovery from actual native plans, receipts and cycle outcomes without inspecting natural-language keywords.</summary>
public static class CognitiveTaskRoutingPolicy
{
    /// <summary>Counts distinct substantive successful operations; polling, plans and cosmetic responses cannot manufacture progress.</summary>
    public static int ConfirmedProgress(CognitiveTaskCheckpoint checkpoint) => checkpoint.Operations
        .Where(operation => operation.Status == "success" && !IsMetadata(operation.Tool)).Select(operation => operation.Fingerprint).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Evaluates immutable native state; provider failures and tool errors take priority over complexity-based escalation.</summary>
    public static CognitiveTaskRouting Evaluate(CognitiveTaskCheckpoint? checkpoint)
    {
        if (checkpoint is null) { return new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.Routine); }
        string prior = checkpoint.Rounds?.Provider ?? "";
        if (checkpoint.HasUncertainEffect || checkpoint.State == "blocked")
        { return new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.OwnerReview, CognitiveRecoveryAction.AwaitOwner, prior); }
        if (checkpoint.Rounds?.LastFailure is { } failure)
        {
            return failure switch
            {
                ConversationFailureKind.Timeout => new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.ModelTimeout, CognitiveRecoveryAction.SplitStep, prior),
                ConversationFailureKind.ContextExceeded => new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.ContextLimit, CognitiveRecoveryAction.ReduceContext, prior),
                ConversationFailureKind.Network or ConversationFailureKind.Unavailable or ConversationFailureKind.CapacityLimit => new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.ProviderCondition, CognitiveRecoveryAction.AwaitProvider, prior),
                _ => new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.OwnerReview, CognitiveRecoveryAction.AwaitOwner, prior)
            };
        }
        var substantive = checkpoint.Operations.Where(operation => !IsMetadata(operation.Tool)).ToArray();
        if (substantive.LastOrDefault()?.Status == "failed")
        { return new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.NativeFailure, CognitiveRecoveryAction.InspectTool, prior); }
        bool pending = checkpoint.Steps.Any(step => step.State == "pending");
        if (pending && checkpoint.Rounds is { StalledCycles: >= 2, ConfirmedProgress: >= 2 })
        { return new(CognitiveTaskDemand.Analysis, CognitiveRoutingReason.VerifiedStall); }
        if (checkpoint.RepositoryStudies is { Count: > 0 } && checkpoint.Rounds?.Cycles > 1)
        {
            int modules = checkpoint.Operations.Count(operation => operation.Tool == "record_repository_analysis" && operation.Status == "success");
            return modules >= 2 ? new(CognitiveTaskDemand.Synthesis, CognitiveRoutingReason.ModuleSynthesis)
                : new(CognitiveTaskDemand.Analysis, CognitiveRoutingReason.RepositoryAnalysis);
        }
        if (pending && checkpoint.Steps.Count >= 4 && checkpoint.Steps.Select(step => step.Tool).Distinct(StringComparer.Ordinal).Count() >= 2
            && substantive.Any(operation => operation.Status == "success") && checkpoint.Rounds?.Cycles > 1)
        { return new(CognitiveTaskDemand.Planning, CognitiveRoutingReason.ComplexPlan); }
        return new(CognitiveTaskDemand.Routine, CognitiveRoutingReason.Routine);
    }

    /// <summary>Identifies changing metadata that cannot demonstrate technical advancement merely by being polled.</summary>
    private static bool IsMetadata(string tool) => tool is "task_status" or "recent_task_context" or "list_capabilities" or "plan_task"
        or "repository_study_status" or "repository_learning_status" or "program_status" or "system_status";
}

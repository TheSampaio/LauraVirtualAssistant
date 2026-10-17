namespace Laura.Core.Conversation;

/// <summary>Native task-stage requirements; derived from observed task state, never from keyword matching or model permission claims.</summary>
public enum CognitiveTaskDemand
{
    /// <summary>No structured evidence requires a secondary provider.</summary>
    Routine,
    /// <summary>A persisted multistep plan requires bounded planning.</summary>
    Planning,
    /// <summary>Actual acquired source evidence requires technical interpretation.</summary>
    Analysis,
    /// <summary>Observed module results are ready for evidence-based synthesis.</summary>
    Synthesis
}

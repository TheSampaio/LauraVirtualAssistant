namespace Laura.Core.Brain;

/// <summary>A completed native process observation emitted by an execution adapter, never parsed from model claims.</summary>
public sealed record BrainExecutionObservation(Guid Id, string Subject, int ExitCode, string OperationHash, DateTimeOffset Timestamp);

/// <summary>A selected execution receipt; its payload contains no command arguments, output, credentials or screenshots.</summary>
public sealed record PendingBrainObservation(Guid Id, string Payload);

/// <summary>Durable learning data for an actual execution observation; goal achievement remains independently unverified.</summary>
public sealed record BrainObservationReceipt(BrainExecutionObservation Observation, string UserGoal);

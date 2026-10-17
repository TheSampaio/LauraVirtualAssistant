namespace Laura.Core.Permissions;

/// <summary>Approval behavior chosen only through local application settings.</summary>
public enum ApprovalMode
{
    /// <summary>Ask for every ungranted protected effect.</summary>
    Ask,
    /// <summary>Also honor explicit directory and bounded installed-application opening policies.</summary>
    Automatic,
    /// <summary>Use the current Windows account for requested operations without per-operation approval.</summary>
    FullAccess
}
/// <summary>When the existing voice service announces a pending request.</summary>
public enum ApprovalVoiceMode
{
    /// <summary>Announce every new request when voice is available.</summary>
    Always,
    /// <summary>Announce only when the main window is hidden or minimized.</summary>
    WhenHidden,
    /// <summary>Use visual notifications only.</summary>
    Never
}
/// <summary>Lifetime of a narrowly scoped approval.</summary>
public enum ApprovalChoice
{
    /// <summary>Stop this action.</summary>
    Deny,
    /// <summary>Approve this invocation only.</summary>
    Once,
    /// <summary>Remember a bounded local grant until exit.</summary>
    Session,
    /// <summary>Persist a bounded local grant.</summary>
    Persistent
}
/// <summary>Deterministic impact classification, independent of model output.</summary>
public enum PermissionRisk
{
    /// <summary>Internal operations with no protected effects.</summary>
    Low,
    /// <summary>Bounded local and reversible data operations.</summary>
    Moderate,
    /// <summary>Processes, system changes and consequential mutations.</summary>
    High,
    /// <summary>Private disclosure, arbitrary input or potentially irreversible effects.</summary>
    Critical
}
/// <summary>Final state recorded without storing action payloads.</summary>
public enum ApprovalOutcome
{
    /// <summary>The user declined.</summary>
    Denied,
    /// <summary>The local UI approved dispatch.</summary>
    Approved,
    /// <summary>A code policy or existing grant approved dispatch.</summary>
    Automatic,
    /// <summary>The request was cancelled before completion.</summary>
    Cancelled,
    /// <summary>The approval deadline elapsed.</summary>
    Expired,
    /// <summary>A technical policy rejected dispatch.</summary>
    Blocked,
    /// <summary>The effect or persistence failed.</summary>
    Failed
}
/// <summary>Persisted in settings.ini, outside the Markdown vault.</summary>
public sealed record PermissionOptions
{
    /// <summary>Gets the safe default approval mode.</summary>
    public ApprovalMode Mode { get; init; }
    /// <summary>Allows ordinary installed GUI applications without arguments only in automatic mode; native code verifies the installation and executable.</summary>
    public bool OpenInstalledApplications { get; init; } = true;
    /// <summary>Gets announcement behavior; never enables microphone authorization.</summary>
    public ApprovalVoiceMode Voice { get; init; } = ApprovalVoiceMode.WhenHidden;
    /// <summary>Gets whether generic native tray notifications are enabled.</summary>
    public bool Notifications { get; init; } = true;
    /// <summary>Gets the lifetime of pending requests, including time spent in the queue.</summary>
    public int TimeoutSeconds { get; init; } = 120;
    /// <summary>Gets audit retention in days; zero disables retained history.</summary>
    public int HistoryDays { get; init; } = 30;
    /// <summary>Rejects invalid enum values and bounds storage and waiting time.</summary>
    public PermissionOptions Sanitized() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : ApprovalMode.Ask,
        Voice = Enum.IsDefined(Voice) ? Voice : ApprovalVoiceMode.Never,
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 10, 600), HistoryDays = Math.Clamp(HistoryDays, 0, 365)
    };
}

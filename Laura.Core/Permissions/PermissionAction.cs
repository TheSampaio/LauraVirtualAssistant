using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Laura.Core.Permissions;

/// <summary>Known effects. Unknown operations fail closed even after a UI decision.</summary>
public enum PermissionOperation
{
    /// <summary>Unregistered effects are denied.</summary>
    Unknown,
    /// <summary>Established internal operations with no OS effects.</summary>
    Internal,
    /// <summary>Read a file for disclosure to a cloud model.</summary>
    FileReadCloud,
    /// <summary>Create or reversibly edit a file.</summary>
    FileWrite,
    /// <summary>Create a directory.</summary>
    FileCreateDirectory,
    /// <summary>Copy a resource.</summary>
    FileCopy,
    /// <summary>Move a resource.</summary>
    FileMove,
    /// <summary>Move a resource to reversible application trash.</summary>
    FileTrash,
    /// <summary>Execute a specific application and arguments.</summary>
    Process,
    /// <summary>Navigate to a concrete external URL.</summary>
    ExternalNavigation,
    /// <summary>Inject input into another application.</summary>
    DesktopInput,
    /// <summary>Change an OS setting or end the user session.</summary>
    SystemSettings,
    /// <summary>Capture and disclose the screen to a cloud model.</summary>
    ScreenCloud,
    /// <summary>Capture and disclose a camera frame.</summary>
    CameraCloud,
    /// <summary>Read the clipboard for cloud disclosure.</summary>
    ClipboardCloud,
    /// <summary>Write literal clipboard text.</summary>
    ClipboardWrite,
    /// <summary>Review a hash-pinned Brain skill without granting tools.</summary>
    SkillActivation,
    /// <summary>Reverse an earlier mutation.</summary>
    Undo,
    /// <summary>Capture locally without disclosure.</summary>
    LocalCapture,
    /// <summary>Export an explicitly selected screenshot locally.</summary>
    ScreenshotExport,
    /// <summary>Attach an explicitly selected image through bounded Brain APIs.</summary>
    BrainAttachment,
    /// <summary>Transmit an exact image to a known cloud model destination.</summary>
    ImageCloud,
    /// <summary>Open a code-verified installed GUI application without arguments, elevation or command execution.</summary>
    ApplicationLaunch,
    /// <summary>Launch or invoke an external MCP server whose effects cannot be safely inferred from advertised annotations.</summary>
    ExternalTool,
    /// <summary>Read local project data without a remote recipient.</summary>
    FileReadLocal,
    /// <summary>Focus or reposition an identity-pinned native window without injecting input.</summary>
    WindowManagement,
    /// <summary>Ask an identity-pinned application window to close normally; unsaved state may require user interaction.</summary>
    ApplicationClose,
    /// <summary>Review a temporary task workspace and its explicit disclosure recipient.</summary>
    WorkspaceAuthorization,
    /// <summary>Create a generated plain-text note in Laura's fixed workspace without accepting a caller-supplied filename.</summary>
    WorkspaceNote,
    /// <summary>Review bounded repository study, Brain retention and the actual model recipient for one task.</summary>
    RepositoryStudyAuthorization,
    /// <summary>Read only a previously reviewed repository scope as untrusted knowledge; never execute source code.</summary>
    RepositoryStudyRead
}

/// <summary>Immutable execution identity constructed by trusted adapters, never deserialized from a model.</summary>
public sealed class PermissionAction
{
    /// <summary>Snapshots relevant parameters. Only their SHA-256 digest enters the audit/store.</summary>
    public PermissionAction(PermissionOperation operation, string origin, string description,
        IEnumerable<string>? resources = null, string parameters = "", string destination = "", bool userInitiated = false, bool automaticEligible = false, Guid taskId = default, string scopeIdentity = "", Guid operationId = default)
    {
        Operation = operation; Origin = origin; Description = description; Destination = destination;
        UserInitiated = userInitiated;
        AutomaticEligible = automaticEligible;
        TaskId = taskId;
        OperationId = operationId;
        ScopeIdentity = scopeIdentity;
        Resources = (resources ?? []).ToImmutableArray();
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { operation, origin, resources = Resources, parameters, destination, userInitiated }))));
        if (scopeIdentity.Length > 0) { Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Fingerprint + ":" + scopeIdentity))); }
    }
    /// <summary>Gets a native repository scope identity; persisted/model content cannot authorize it.</summary>
    public string ScopeIdentity { get; }
    /// <summary>Gets the code-defined effect.</summary>
    public PermissionOperation Operation { get; }
    /// <summary>Gets the trusted adapter origin.</summary>
    public string Origin { get; }
    /// <summary>Gets local display details; never persisted or spoken verbatim.</summary>
    public string Description { get; }
    /// <summary>Gets all affected canonical file resources.</summary>
    public ImmutableArray<string> Resources { get; }
    /// <summary>Gets the actual external destination, including cloud disclosure.</summary>
    public string Destination { get; }
    /// <summary>Gets the exact operation identity, including content and command arguments.</summary>
    public string Fingerprint { get; }
    /// <summary>Gets a trusted native gesture marker. Tool adapters never read this marker from model JSON.</summary>
    public bool UserInitiated { get; }
    /// <summary>Gets native installation-policy evidence; model JSON is never allowed to supply this value.</summary>
    public bool AutomaticEligible { get; }
    /// <summary>Gets the native active task identity, never read from a model-supplied argument.</summary>
    public Guid TaskId { get; }
    /// <summary>Gets the native effect identity used to correlate authorization with execution.</summary>
    public Guid OperationId { get; }
}

/// <summary>A pending immutable request. Its ID accepts exactly one timely UI decision.</summary>
public sealed record ApprovalRequest(Guid Id, PermissionAction Action, PermissionRisk Risk,
    DateTimeOffset Created, DateTimeOffset Expires, bool CanRemember);
/// <summary>A revocable exact operation grant. Directory scopes are limited to one file operation and origin.</summary>
public sealed record PermissionGrant(Guid Id, PermissionOperation Operation, string Origin, string Scope,
    string Fingerprint, DateTimeOffset Created, bool Persistent, bool DirectoryScope)
{
    /// <summary>Gets an explicitly reviewed task identity; ordinary grants have no task scope.</summary>
    public Guid TaskId { get; init; }
    /// <summary>Gets the task grant deadline; task scopes never survive process restart.</summary>
    public DateTimeOffset? Expires { get; init; }
    /// <summary>Gets the explicitly reviewed disclosure host, empty for strictly local workspace access.</summary>
    public string Destination { get; init; } = "";
    /// <summary>Gets whether the scope came from full-access policy rather than a separate native owner review.</summary>
    public bool FullAccessDerived { get; init; }
}
/// <summary>Minimal audit data; no prompts, clipboard, file contents, URLs, arguments, credentials or tokens.</summary>
public sealed record ApprovalAudit(Guid Id, DateTimeOffset Time, PermissionOperation Operation,
    string Origin, PermissionRisk Risk, ApprovalOutcome Outcome, string Scope, string Reason)
{
    /// <summary>Gets the native task identity without storing its prompt or objective.</summary>
    public Guid TaskId { get; init; }
}
/// <summary>Denial stops execution rather than returning a reusable approval token.</summary>
public sealed class PermissionDeniedException(string reason) : InvalidOperationException(reason);

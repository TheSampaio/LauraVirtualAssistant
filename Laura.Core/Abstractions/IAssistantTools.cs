using System.Text.Json;

namespace Laura.Core.Abstractions;

/// <summary>Publishes structured, implemented computer capabilities to the live model.</summary>
public interface IAssistantTools
{
    /// <summary>Gets the public function schemas for available adapters.</summary>
    object[] Declarations { get; }
    /// <summary>Executes one structured action with native confirmation where required.</summary>
    Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>Returns observable tool output and an optional explicitly captured vision frame.</summary>
public sealed record AssistantToolResult(string Text, byte[]? Image = null, string MimeType = "image/jpeg")
{
    /// <summary>Cancels future disclosure if the originating editable capture is closed.</summary>
    public CancellationToken ImageLifetime { get; init; }
}

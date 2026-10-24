using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>Adapts a text inference protocol without owning tools, memory, audio or permission policy.</summary>
public interface ITextModelClient
{
    /// <summary>Gets the trusted native provider identifier.</summary>
    string Provider { get; }
    /// <summary>Gets the configured model identifier.</summary>
    string Model { get; }
    /// <summary>Gets whether explicit configuration and required credentials or local assets are available.</summary>
    bool IsAvailable { get; }
    /// <summary>Gets whether this adapter supports explicitly attached images.</summary>
    bool SupportsImages { get; }
    /// <summary>Gets the configured context capacity; small runtimes use progressive native schema discovery.</summary>
    int ContextTokenLimit => int.MaxValue;
    /// <summary>Runs one bounded inference round. Args: request: Shared text and tool context; token: Cancellation. Returns: Visible text and inert tool requests.</summary>
    Task<TextModelReply> CompleteAsync(TextModelRequest request, CancellationToken token);
}

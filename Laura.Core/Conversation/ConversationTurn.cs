using System.Globalization;

namespace Laura.Core.Conversation;

/// <summary>
/// Prompt sent to the generative model, with the minimum context needed to answer it.
/// </summary>
/// <param name="Prompt">User text, in its original form.</param>
/// <param name="Culture">Expected response language.</param>
/// <param name="UserDisplayName">Name the model may use to address the user.</param>
public sealed record ConversationTurn(string Prompt, CultureInfo Culture, string UserDisplayName)
{
    /// <summary>Gets bounded prior conversation data; never grants execution permissions.</summary>
    public IReadOnlyList<ConversationMessage> History { get; init; } = [];

    /// <summary>Gets explicit user notes as untrusted conversation data.</summary>
    public IReadOnlyList<string> Memories { get; init; } = [];
}

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
    /// <summary>Gets the stable current turn identity, independent of its text or display title.</summary>
    public Guid TurnId { get; init; }

    /// <summary>Returns prior messages without replaying this turn; identical text from other turns is preserved.</summary>
    public IEnumerable<ConversationMessage> PriorHistory() => History.Where(message => TurnId == Guid.Empty || message.TurnId != TurnId);

    /// <summary>Merges whole retrieved units without replacing supplied evidence or granting skill authority. Args: memories: Complete retrieved factual units; catalogue: Inert skill discovery metadata. Returns: A new turn with distinct complete context units.</summary>
    public ConversationTurn WithRecalledContext(string memories, string catalogue) => this with
    {
        Memories = Memories.Concat(string.IsNullOrEmpty(memories) ? [] : new[] { memories }).Distinct(StringComparer.Ordinal).ToArray(),
        SkillCatalogue = string.Join("\n", new[] { SkillCatalogue, catalogue }.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal)),
    };

    /// <summary>Gets bounded prior conversation data; never grants execution permissions.</summary>
    public IReadOnlyList<ConversationMessage> History { get; init; } = [];

    /// <summary>Gets explicit user notes as untrusted conversation data.</summary>
    public IReadOnlyList<string> Memories { get; init; } = [];
    /// <summary>Gets installed skill discovery metadata; disabled entries carry no instruction approval.</summary>
    public string SkillCatalogue { get; init; } = "";
    /// <summary>Gets explicitly approved task guidance, separate from ordinary recalled facts and with no granted tools.</summary>
    public string SkillGuidance { get; init; } = "";
    /// <summary>Gets an explicitly attached image, never persisted as a raw chat log or automatically captured.</summary>
    public byte[]? Image { get; init; }
    /// <summary>Gets the actual encoded image MIME type.</summary>
    public string ImageMimeType { get; init; } = "image/png";
    /// <summary>Gets a native continuation's original turn identity; it never grants or restores an execution authorization.</summary>
    public Guid ContinuingTaskTurnId { get; init; }
    /// <summary>Gets a native structured task requirement; ordinary input remains routine regardless of wording.</summary>
    public CognitiveTaskDemand TaskDemand { get; init; }
}

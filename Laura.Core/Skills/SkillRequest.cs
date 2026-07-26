using System.Globalization;
using Laura.Core.Text;

namespace Laura.Core.Skills;

/// <summary>
/// A command directed at the assistant, ready to be evaluated by the skills.
/// </summary>
public sealed record SkillRequest
{
    /// <summary>Gets the text as it came from the source, kept for logging and the generative AI.</summary>
    public required string RawText { get; init; }

    /// <summary>Gets the text in the canonical form used for comparisons.</summary>
    public required string NormalizedText { get; init; }

    /// <summary>Gets the culture the command was given in.</summary>
    public required CultureInfo Culture { get; init; }

    /// <summary>Gets the instant the command arrived.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the origin of the command.</summary>
    public required SkillRequestSource Source { get; init; }

    /// <summary>
    /// Builds a request from raw text, normalizing it.
    ///
    /// Args:
    ///     text: Command text.
    ///     culture: Culture the command was given in.
    ///     timestamp: Instant of the command.
    ///     source: Origin of the command.
    /// Returns:
    ///     A request with <see cref="NormalizedText"/> already computed.
    /// </summary>
    public static SkillRequest Create(
        string text,
        CultureInfo culture,
        DateTimeOffset timestamp,
        SkillRequestSource source) => new()
        {
            RawText = text,
            NormalizedText = TextNormalizer.Normalize(text),
            Culture = culture,
            Timestamp = timestamp,
            Source = source,
        };
}

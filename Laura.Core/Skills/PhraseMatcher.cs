using Laura.Core.Text;

namespace Laura.Core.Skills;

/// <summary>
/// Matches commands against the phrase lists from the language files.
///
/// Pure functions, so the recognition rule is testable without involving a
/// microphone, localization or any concrete skill.
/// </summary>
public static class PhraseMatcher
{
    /// <summary>
    /// Reports whether the text contains any of the given phrases.
    ///
    /// Args:
    ///     normalizedText: The already-normalized command.
    ///     phrases: Candidate phrases, normalized internally.
    ///
    /// Returns:
    ///     <see langword="true"/> when at least one phrase appears in the text.
    /// </summary>
    public static bool MatchesAny(string normalizedText, IReadOnlyList<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);

        if (string.IsNullOrEmpty(normalizedText))
        {
            return false;
        }

        foreach (string phrase in phrases)
        {
            if (TextNormalizer.ContainsPhrase(normalizedText, TextNormalizer.Normalize(phrase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a command prefix and returns what comes after it.
    ///
    /// Prefixes are tested from longest to shortest, so "search for" beats "search"
    /// and the argument does not come out contaminated.
    ///
    /// Args:
    ///     normalizedText: The already-normalized command.
    ///     prefixes: Candidate prefixes, normalized internally.
    ///     argument: Receives the text after the prefix, or an empty string.
    ///
    /// Returns:
    ///     <see langword="true"/> when a prefix was found.
    /// </summary>
    public static bool TryMatchPrefix(
        string normalizedText,
        IReadOnlyList<string> prefixes,
        out string argument)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        argument = string.Empty;

        if (string.IsNullOrEmpty(normalizedText))
        {
            return false;
        }

        IEnumerable<string> ordered = prefixes
            .Select(TextNormalizer.Normalize)
            .Where(static prefix => prefix.Length > 0)
            .OrderByDescending(static prefix => prefix.Length);

        foreach (string prefix in ordered)
        {
            if (!TextNormalizer.ContainsPhrase(normalizedText, prefix))
            {
                continue;
            }

            argument = TextNormalizer.RemovePhrase(normalizedText, prefix);
            return true;
        }

        return false;
    }
}

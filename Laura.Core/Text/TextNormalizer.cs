using System.Globalization;
using System.Text;

namespace Laura.Core.Text;

/// <summary>
/// Normalizes command text for comparisons tolerant of accents, casing, and punctuation.
///
/// User input may include variations irrelevant to command matching. Every phrase
/// comparison in the domain goes through here to operate on one canonical form.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Converts text to the canonical form used in command comparisons.
    ///
    /// Removes diacritics, converts to lowercase using the invariant culture,
    /// drops punctuation, and collapses consecutive whitespace.
    ///
    /// Args:
    ///     text: Raw text from the user. May be null or empty.
    ///
    /// Returns:
    ///     The canonical text, or an empty string when the input contains
    ///     no meaningful characters.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool previousWasSeparator = false;

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            // Punctuation and spaces become one separator, never at the start.
            if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Indicates whether the text contains the given phrase as a complete word sequence.
    ///
    /// Unlike <see cref="string.Contains(string, StringComparison)"/>, this avoids
    /// false positives in prefixes ("laura" does not match inside "lauraceas").
    ///
    /// Args:
    ///     text: Already-normalized text to search in.
    ///     phrase: Already-normalized phrase to search for.
    ///
    /// Returns:
    ///     <see langword="true"/> when the phrase appears delimited by word
    ///     boundaries; otherwise, <see langword="false"/>.
    /// </summary>
    public static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase))
        {
            return false;
        }

        int index = text.IndexOf(phrase, StringComparison.Ordinal);

        while (index >= 0)
        {
            bool startsAtBoundary = index == 0 || text[index - 1] == ' ';
            int endIndex = index + phrase.Length;
            bool endsAtBoundary = endIndex == text.Length || text[endIndex] == ' ';

            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            index = text.IndexOf(phrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Removes the first occurrence of the phrase and returns the remaining text.
    ///
    /// Used to separate a trigger from the command content.
    ///
    /// Args:
    ///     text: Already-normalized text.
    ///     phrase: Already-normalized phrase to remove.
    ///
    /// Returns:
    ///     The text without the phrase and without surrounding spaces. When the phrase is not
    ///     found, returns the original text unchanged.
    /// </summary>
    public static string RemovePhrase(string text, string phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase))
        {
            return text;
        }

        int index = text.IndexOf(phrase, StringComparison.Ordinal);

        return index < 0
            ? text
            : string.Concat(text.AsSpan(0, index), " ", text.AsSpan(index + phrase.Length)).Trim();
    }
}

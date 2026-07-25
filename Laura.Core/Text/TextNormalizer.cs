using System.Globalization;
using System.Text;

namespace Laura.Core.Text;

/// <summary>
/// Normalizes spoken text for comparisons tolerant of accents, casing, and punctuation.
///
/// The speech recognizer returns transcriptions with variations irrelevant to
/// command matching ("Ok, Laura!" and "ok laura" are the same intent). Every
/// phrase comparison in the domain goes through here to operate on one canonical form.
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
    ///     text: Texto bruto transcrito pelo reconhecedor. Pode ser nulo ou vazio.
    ///
    /// Returns:
    ///     The canonical text, or an empty string when the input contains
    ///     nenhum caractere significativo.
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
    ///     <see langword="true"/> quando a frase aparece delimitada por fronteiras
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
    /// Used to separate the trigger from the command content, as in
    /// "ok laura what time is it" where the trigger must be discarded.
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

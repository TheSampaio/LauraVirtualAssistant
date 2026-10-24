using System.Text.RegularExpressions;

namespace Laura.Core.Speech;

/// <summary>Derives a pronounceable speech view without modifying the written model response.</summary>
public static partial class SpokenTextFormatter
{
    /// <summary>Removes code blocks, link targets and common Markdown marks from spoken output.</summary>
    public static string Format(string writtenText)
    {
        if (string.IsNullOrWhiteSpace(writtenText)) { return string.Empty; }
        string spoken = CodeFence().Replace(writtenText, " ");
        spoken = MarkdownLink().Replace(spoken, "$1");
        spoken = RawUrl().Replace(spoken, " ");
        spoken = LinePrefix().Replace(spoken, "");
        spoken = Decoration().Replace(spoken, "");
        spoken = InlineCode().Replace(spoken, "$1");
        spoken = TableDivider().Replace(spoken, " ").Replace('|', ',');
        spoken = string.Concat(spoken.EnumerateRunes().Where(rune => System.Text.Rune.GetUnicodeCategory(rune) != System.Globalization.UnicodeCategory.OtherSymbol).Select(rune => rune.ToString()));
        spoken = Whitespace().Replace(spoken, " ").Trim();
        return spoken;
    }
    /// <summary>Segments pronounceable prose to bound native synthesis memory and keep cancellation responsive without truncating text.</summary>
    public static IEnumerable<string> Segments(string writtenText, int maximumCharacters = 400)
    {
        string text = Format(writtenText); maximumCharacters = Math.Clamp(maximumCharacters, 100, 1000);
        while (text.Length > maximumCharacters)
        {
            int boundary = text.LastIndexOfAny(['.', '!', '?', ';'], maximumCharacters - 1, maximumCharacters / 2);
            if (boundary < 0) { boundary = text.LastIndexOf(' ', maximumCharacters - 1); }
            int length = boundary > 0 ? boundary + 1 : maximumCharacters;
            yield return text[..length].Trim(); text = text[length..].TrimStart();
        }
        if (text.Length > 0) { yield return text; }
    }

    /// <summary>Matches complete fenced code so source formatting stays in written output only.</summary>
    [GeneratedRegex(@"(?ms)^\s*```[^\r\n]*\r?\n.*?^\s*```\s*$")]
    private static partial Regex CodeFence();
    /// <summary>Separates readable link labels from unpronounceable destinations.</summary>
    [GeneratedRegex(@"\[([^\]\r\n]+)\]\((?:https?://|mailto:)[^\s)]+\)")]
    private static partial Regex MarkdownLink();
    /// <summary>Matches raw web addresses without changing the written source.</summary>
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex RawUrl();
    /// <summary>Matches structural heading, list and quotation prefixes.</summary>
    [GeneratedRegex(@"(?m)^\s{0,3}(?:#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s*)")]
    private static partial Regex LinePrefix();
    /// <summary>Matches prose decoration while retaining word-internal characters.</summary>
    [GeneratedRegex(@"(?<!\w)[*_~]{1,2}|[*_~]{1,2}(?!\w)")]
    private static partial Regex Decoration();
    /// <summary>Preserves inline technical labels without reading their Markdown delimiters.</summary>
    [GeneratedRegex(@"`([^`\r\n]+)`")]
    private static partial Regex InlineCode();
    /// <summary>Matches table alignment rows that have no conversational meaning.</summary>
    [GeneratedRegex(@"(?m)^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$")]
    private static partial Regex TableDivider();
    /// <summary>Normalizes layout whitespace for the local speech renderer.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

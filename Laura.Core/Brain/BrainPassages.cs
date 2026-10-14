using System.Globalization;
using Markdig;
using Markdig.Syntax;

namespace Laura.Core.Brain;

/// <summary>A disposable retrieval location in an authored Markdown note; it is not a new knowledge node.</summary>
public sealed record BrainPassage(string Id, string NoteId, int Start, int Length, string Heading);

/// <summary>Builds bounded heading-aware source spans without modifying Markdown or creating graph relations.</summary>
public static class BrainPassages
{
    private const int MaxPassages = 512;
    /// <summary>Segments source text with small overlaps and Markdown heading context. The final span always covers the tail.</summary>
    public static IReadOnlyList<BrainPassage> Split(BrainNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        string body = note.Body; if (body.Length == 0) { return [new(note.Id + ":0", note.Id, 0, 0, "")]; }
        var headings = new List<(int Start, string Context)>(); var hierarchy = new SortedDictionary<int, string>();
        foreach (HeadingBlock block in Markdown.Parse(body).OfType<HeadingBlock>().Take(2048))
        {
            foreach (int level in hierarchy.Keys.Where(level => level >= block.Level).ToArray()) { hierarchy.Remove(level); }
            string text = body[block.Span.Start..(block.Span.End + 1)].Trim().Trim('#').Trim();
            hierarchy[block.Level] = text.Length > 100 ? text[..100] : text;
            string context = string.Join(" / ", hierarchy.Values); headings.Add((block.Span.Start, context.Length > 240 ? context[..240] : context));
        }
        var result = new List<BrainPassage>(); int start = 0, headingIndex = -1;
        int target = Math.Max(600, (int)Math.Ceiling(body.Length / (MaxPassages * .65)));
        while (start < body.Length)
        {
            while (headingIndex + 1 < headings.Count && headings[headingIndex + 1].Start <= start) { headingIndex++; }
            int end = result.Count == MaxPassages - 1 ? body.Length : Math.Min(body.Length, start + target);
            bool headingBreak = result.Count < MaxPassages - 1 && headingIndex + 1 < headings.Count && headings[headingIndex + 1].Start < end;
            if (headingBreak) { end = headings[headingIndex + 1].Start; }
            else if (end < body.Length)
            {
                int boundary = body.LastIndexOf('\n', end - 1, Math.Max(1, (end - start) / 2));
                if (boundary > start) { end = boundary + 1; }
                if (end > start && char.IsHighSurrogate(body[end - 1])) { end--; }
            }
            string heading = headingIndex >= 0 ? headings[headingIndex].Context : "";
            // A chunk can begin before its first heading; expose that section rather than an unrelated earlier one.
            if (headingIndex + 1 < headings.Count && headings[headingIndex + 1].Start < end) { heading = headings[headingIndex + 1].Context; }
            result.Add(new(note.Id + ":" + result.Count.ToString(CultureInfo.InvariantCulture), note.Id, start, end - start, heading));
            if (end == body.Length) { break; }
            int next = headingBreak ? end : Math.Max(start + 1, end - 80);
            if (char.IsLowSurrogate(body[next])) { next++; }
            start = next;
        }
        return result;
    }
    /// <summary>Selects source text near matching terms inside the chosen passage, retaining real offsets for progressive inspection.</summary>
    public static (string Text, int Start) Excerpt(BrainNote note, BrainPassage? passage, IReadOnlyList<string> terms, int limit)
    {
        string body = note.Body; int lower = Math.Clamp(passage?.Start ?? 0, 0, body.Length), upper = Math.Min(body.Length, lower + (passage?.Length ?? body.Length));
        string region = body[lower..upper]; var positions = new List<(int Position, string Term)>();
        foreach (string term in terms)
        {
            int cursor = 0;
            for (int count = 0; count < 16 && cursor < region.Length; count++)
            { int found = region.IndexOf(term, cursor, StringComparison.OrdinalIgnoreCase); if (found < 0) { break; } positions.Add((found, term)); cursor = found + term.Length; }
        }
        int best = 0, coverage = -1;
        foreach (int position in positions.Select(item => item.Position).Prepend(0))
        {
            int candidate = Math.Max(0, position - 100);
            int matches = positions.Where(item => item.Position >= candidate && item.Position < candidate + limit).Select(item => item.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (matches > coverage) { coverage = matches; best = candidate; }
        }
        int start = lower + best, length = Math.Min(limit, upper - start);
        if (start > 0 && start < body.Length && char.IsLowSurrogate(body[start])) { start++; length--; }
        if (length > 0 && start + length < body.Length && char.IsHighSurrogate(body[start + length - 1])) { length--; }
        return (body.Substring(start, Math.Max(0, length)), start);
    }
}

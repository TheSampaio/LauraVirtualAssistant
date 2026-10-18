using System.Text;

namespace Laura.Core.Conversation;

/// <summary>Suppresses the model's initial private thinking block even when delimiters arrive across token boundaries.</summary>
public sealed class VisibleModelOutput(bool startsInsideThinking = false)
{
    private readonly StringBuilder _pending = new();
    private int _state = startsInsideThinking ? 1 : 0;

    /// <summary>Returns only newly available visible answer text. Args: text: Decoded generation chunk. Returns: Visible suffix, empty while a private block is incomplete. Raises: InvalidOperationException for excessive hidden output.</summary>
    public string Append(string text)
    {
        if (_state == 2) { return text; }
        _pending.Append(text);
        if (_pending.Length > 128_000) { throw new InvalidOperationException("Private generation exceeded its output budget."); }
        string pending = _pending.ToString();
        if (_state == 0)
        {
            string candidate = pending.TrimStart();
            if (candidate.Length == 0 || "<think>".StartsWith(candidate, StringComparison.Ordinal)) { return ""; }
            if (!candidate.StartsWith("<think>", StringComparison.Ordinal)) { _state = 2; _pending.Clear(); return pending; }
            pending = candidate[7..]; _pending.Clear(); _pending.Append(pending); _state = 1;
        }
        int end = pending.IndexOf("</think>", StringComparison.Ordinal);
        if (end < 0) { return ""; }
        _state = 2; _pending.Clear(); return pending[(end + 8)..].TrimStart();
    }

    /// <summary>Gets whether the generation ended inside a private block or an incomplete delimiter.</summary>
    public bool Incomplete => _state != 2;
}

using Laura.Core.Text;

namespace Laura.Core.Skills;

/// <summary>Matches spoken application names against discovered installed-program names.</summary>
public static class ApplicationNameMatcher
{
    /// <summary>Normalizes common speech aliases without accepting shell syntax.</summary>
    public static string Normalize(string name)
    {
        string normalized = TextNormalizer.Normalize(name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Replace(".lnk", "", StringComparison.OrdinalIgnoreCase));
        if (TextNormalizer.ContainsPhrase(normalized, "ps2") || normalized.Contains("playstation 2", StringComparison.Ordinal)) { return "pcsx2"; }
        string[] fillers = ["meu", "minha", "o", "a", "do", "da", "de", "my", "the", "app", "aplicativo", "programa", "emulador"];
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !fillers.Contains(word, StringComparer.Ordinal)));
    }
    /// <summary>Scores exact and whole-name matches, avoiding short substring collisions.</summary>
    public static int Score(string candidate, string requested)
    {
        string query = Normalize(requested), name = Normalize(candidate);
        if (query.Length < 2 || name.Length == 0) { return 0; }
        if (name == query) { return 100; }
        if (name.StartsWith(query + " ", StringComparison.Ordinal) || name.StartsWith(query + "-", StringComparison.Ordinal)) { return 85; }
        if ((" " + name + " ").Contains(" " + query + " ", StringComparison.Ordinal)) { return 70; }
        return query.Length >= 4 && name.Contains(query, StringComparison.Ordinal) ? 50 : 0;
    }
}

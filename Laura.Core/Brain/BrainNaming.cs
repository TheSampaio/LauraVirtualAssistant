using System.Text.RegularExpressions;
using Laura.Core.Text;

namespace Laura.Core.Brain;

/// <summary>Portable, human-readable filenames retain a short collision suffix without encoding personal data in opaque paths.</summary>
public static class BrainNaming
{
    /// <summary>Capitalizes newly created directory initials while retaining existing folder spelling and document filenames.</summary>
    public static string DestinationPath(string path, IReadOnlyList<string> existingFolders, bool directory = false)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        int count = directory ? parts.Length : parts.Length - 1;
        for (int index = 0; index < count; index++)
        {
            string candidate = string.Join('/', parts.Take(index + 1));
            string? existing = existingFolders.FirstOrDefault(folder => folder.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { parts[index] = existing.Split('/')[index]; }
            else if (parts[index].Length > 0) { parts[index] = char.ToUpperInvariant(parts[index][0]) + parts[index][1..]; }
        }
        return string.Join('/', parts);
    }
    /// <summary>Combines a bounded readable slug with a stable key suffix.</summary>
    public static string FileName(string title, string stableKey)
    {
        string slug = Regex.Replace(TextNormalizer.Normalize(title), @"[^\p{L}\p{N}]+", "-", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim('-');
        if (slug.Length == 0) { slug = "note"; } if (slug.Length > 50) { slug = slug[..50].TrimEnd('-'); }
        return slug + "-" + FileBrainStorage.Hash(stableKey)[..8].ToLowerInvariant() + ".md";
    }
}

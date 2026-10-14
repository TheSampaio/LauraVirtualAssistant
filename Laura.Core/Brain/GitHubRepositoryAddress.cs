using System.Text.RegularExpressions;

namespace Laura.Core.Brain;

/// <summary>A validated public GitHub repository identity, with an optional explicit branch, tag or commit.</summary>
public sealed record GitHubRepositoryAddress(string Owner, string Repository, string Reference)
{
    /// <summary>Gets the canonical public repository URL without credentials or query parameters.</summary>
    public string Url => "https://github.com/" + Owner + "/" + Repository;

    /// <summary>Accepts repository and tree URLs; ambiguous slash-containing refs require the explicit reference parameter.</summary>
    public static GitHubRepositoryAddress Parse(string url, string reference = "")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || uri.Host != "github.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        { throw new ArgumentException("Use an HTTPS github.com repository URL without credentials, query or fragment."); }
        string[] parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 && (parts.Length != 4 || parts[2] != "tree")) { throw new ArgumentException("Use a repository URL and an explicit reference for nested branch names."); }
        string owner = parts[0], repository = parts[1];
        if (repository.EndsWith(".git", StringComparison.Ordinal)) { repository = repository[..^4]; }
        if (!ValidName(owner) || !ValidName(repository)) { throw new ArgumentException("Invalid GitHub repository identity."); }
        if (parts.Length == 4 && reference.Length == 0) { reference = Uri.UnescapeDataString(parts[3]); }
        if (reference.Length > 200 || reference.Any(char.IsControl) || reference.Contains('\\') || reference.Split('/').Any(part => part is "." or ".."))
        { throw new ArgumentException("Invalid GitHub reference."); }
        return new(owner, repository, reference);
    }

    /// <summary>Restricts API path segments to ordinary GitHub names.</summary>
    private static bool ValidName(string value) => value is { Length: > 0 and <= 100 } && value is not ("." or "..")
        && Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

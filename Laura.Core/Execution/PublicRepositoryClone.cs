using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Laura.Core.Execution;

/// <summary>Defines an exact anonymous HTTPS clone effect without shell text, hooks, templates, credential helpers or repository execution.</summary>
public static partial class PublicRepositoryClone
{
    private static readonly string[] Prefix = ["-c", "core.hooksPath=NUL", "-c", "credential.helper=", "-c", "protocol.allow=never",
        "-c", "protocol.https.allow=always", "-c", "http.followRedirects=false", "-c", "core.attributesFile=NUL", "-c", "core.symlinks=false",
        "clone", "--template=", "--no-recurse-submodules", "--"];
    private static readonly ImmutableDictionary<string, string> Environment = new Dictionary<string, string>
    {
        ["GIT_CONFIG_GLOBAL"] = "NUL", ["GIT_CONFIG_SYSTEM"] = "NUL", ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_COUNT"] = "0", ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_ATTR_NOSYSTEM"] = "1"
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds frozen arguments for a new clone. Args: executable: Resolved installed Git; url: Anonymous GitHub HTTPS repository; target: New absolute directory; taskId: Native owner objective. Returns: Bounded process request. Raises: ArgumentException for unsupported URLs or destinations.</summary>
    public static CommandExecutionRequest Prepare(string executable, string url, string target, Guid taskId)
    {
        if (!RepositoryUrl().IsMatch(url) || !Path.IsPathFullyQualified(target) || Path.GetDirectoryName(target) is not { } parent
            || Path.GetFileName(target).Length == 0) { throw new ArgumentException("Clone requires an anonymous GitHub HTTPS URL and a new absolute directory."); }
        return new CommandExecutionRequest(executable, [.. Prefix, url, Path.GetFullPath(target)], Path.GetFullPath(parent))
            { TaskId = taskId, TimeoutSeconds = 180, EnvironmentVariables = Environment }.Validated();
    }

    /// <summary>Recognizes only the exact bounded native effect, including configuration and task environment. Returns: Whether the request matches and its destination; filesystem and installed-image checks remain native responsibilities.</summary>
    public static bool TryGetTarget(CommandExecutionRequest request, out string target)
    {
        target = "";
        if (request.Background || request.Arguments.Count != Prefix.Length + 2 || !request.Arguments.Take(Prefix.Length).SequenceEqual(Prefix)
            || request.EnvironmentVariables.Count != Environment.Count || Environment.Any(pair => !request.EnvironmentVariables.TryGetValue(pair.Key, out string? value) || value != pair.Value)
            || !RepositoryUrl().IsMatch(request.Arguments[^2]) || !Path.IsPathFullyQualified(request.Arguments[^1])) { return false; }
        string destination = Path.GetFullPath(request.Arguments[^1]);
        if (!string.Equals(Path.GetDirectoryName(destination), request.WorkingDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { return false; }
        target = destination; return true;
    }

    /// <summary>Accepts only literal GitHub repository coordinates without credentials, local transports, redirects or option syntax.</summary>
    [GeneratedRegex(@"\Ahttps://github\.com/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryUrl();
}

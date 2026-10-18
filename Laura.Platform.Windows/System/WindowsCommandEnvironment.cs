using Laura.Core.Permissions;
using System.Text.RegularExpressions;

namespace Laura.Platform.Windows.System;

/// <summary>Refreshes child-process PATH from current Windows settings without changing the owner's environment.</summary>
public static partial class WindowsCommandEnvironment
{
    /// <summary>Sets both PowerShell and native output encoding without loading a profile.</summary>
    public const string PowerShellUtf8Prefix = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $OutputEncoding = [Console]::OutputEncoding; ";

    /// <summary>Pins simple version queries to their resolved installed image while preserving other explicit command text.</summary>
    public static string PreparePowerShellCommand(string command)
    {
        string[] parts = command.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0] is "git" or "node" or "python" or "dotnet" && parts[1] is "--version" or "-v")
        {
            string executable = WindowsProcessLauncher.ResolveExecutable(parts[0]);
            command = "& '" + executable.Replace("'", "''", StringComparison.Ordinal) + "' '" + parts[1] + "'";
        }
        return PowerShellUtf8Prefix + command;
    }

    /// <summary>Recognizes only the literal native version-query form; arbitrary scripts cannot claim read-only effects.</summary>
    public static bool TryGetPowerShellVersionTarget(string script, out string executable, out string argument)
    {
        executable = argument = "";
        if (!script.StartsWith(PowerShellUtf8Prefix, StringComparison.Ordinal)) { return false; }
        Match match = VersionInvocation().Match(script[PowerShellUtf8Prefix.Length..]);
        if (!match.Success) { return false; }
        executable = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal); argument = match.Groups[2].Value;
        return Path.IsPathFullyQualified(executable);
    }

    /// <summary>Matches an exact literal executable/argument pair, including escaped apostrophes.</summary>
    [GeneratedRegex("\\A& '((?:[^']|'')+)' '(--version|-v)'\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionInvocation();

    /// <summary>Returns absolute local search directories in inherited, machine and user order. Invalid entries are ignored.</summary>
    public static IReadOnlyList<string> SearchDirectories()
    {
        var result = new List<string> { Environment.SystemDirectory };
        var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH", target) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string path = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(path) || PermissionPolicy.IsNetworkPath(path)) { continue; }
                try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
                catch (ArgumentException) { continue; }
                if (seen.Add(path)) { result.Add(path); }
            }
        }
        return result;
    }

    /// <summary>Finds the Windows inbox PowerShell host, with optional PowerShell 7 fallback. Returns: Absolute executable path.</summary>
    public static string ResolvePowerShell()
    {
        string inbox = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(inbox) ? WindowsProcessLauncher.ResolveExecutable(inbox) : WindowsProcessLauncher.ResolveExecutable("pwsh.exe");
    }
}

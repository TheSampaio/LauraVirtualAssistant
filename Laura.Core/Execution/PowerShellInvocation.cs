using System.Collections.Immutable;

namespace Laura.Core.Execution;

/// <summary>Normalizes explicit PowerShell host syntax without granting script execution or pretending that a directory is a sandbox.</summary>
public sealed record PowerShellInvocation(CommandExecutionRequest Request, string? ScriptPath)
{
    /// <summary>Requires visible command text or an absolute entry script, disables profiles/interaction and rejects opaque/overriding host options. Args: request: Frozen structured process request. Returns: Actual host arguments and optional script identity. Raises: ArgumentException for unsupported host syntax.</summary>
    public static PowerShellInvocation Prepare(CommandExecutionRequest request)
    {
        request = request.Validated(); string executable = Path.GetFileNameWithoutExtension(request.Executable);
        if (!executable.Equals("pwsh", StringComparison.OrdinalIgnoreCase) && !executable.Equals("powershell", StringComparison.OrdinalIgnoreCase)) { return new(request, null); }
        int modeIndex = -1; bool fileMode = false;
        for (int index = 0; index < request.Arguments.Count; index++)
        {
            string argument = request.Arguments[index];
            if (argument.Equals("-File", StringComparison.OrdinalIgnoreCase) || argument.Equals("-Command", StringComparison.OrdinalIgnoreCase))
            { modeIndex = index; fileMode = argument.Equals("-File", StringComparison.OrdinalIgnoreCase); break; }
            if (!argument.Equals("-NoLogo", StringComparison.OrdinalIgnoreCase) && !argument.Equals("-NoProfile", StringComparison.OrdinalIgnoreCase) && !argument.Equals("-NonInteractive", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("PowerShell requires explicit -File or -Command syntax. Encoded commands, profile loading, interactive sessions and execution-policy overrides are unsupported."); }
        }
        if (modeIndex < 0 || modeIndex + 1 >= request.Arguments.Count || string.IsNullOrWhiteSpace(request.Arguments[modeIndex + 1]) || request.Arguments[modeIndex + 1] == "-")
        { throw new ArgumentException("PowerShell requires an explicit command string or entry script; stdin execution is disabled."); }
        string? script = null;
        if (fileMode)
        {
            script = request.Arguments[modeIndex + 1];
            if (!Path.IsPathFullyQualified(script) || !Path.GetExtension(script).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("PowerShell -File requires an absolute .ps1 entry script."); }
            script = Path.GetFullPath(script);
        }
        else if (modeIndex + 2 != request.Arguments.Count) { throw new ArgumentException("Use one explicit command string; script arguments require -File."); }
        string[] arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", fileMode ? "-File" : "-Command", script ?? request.Arguments[modeIndex + 1], .. request.Arguments.Skip(modeIndex + 2)];
        return new((request with { Arguments = arguments.ToImmutableArray() }).Validated(), script);
    }
}

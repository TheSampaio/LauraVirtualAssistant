using System.Collections.Immutable;

namespace Laura.Core.Execution;

/// <summary>Structured process parameters. A working directory is not an operating-system sandbox.</summary>
public sealed record CommandExecutionRequest(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Gets the bounded execution deadline in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 60;
    /// <summary>Gets the combined retained stdout/stderr character limit.</summary>
    public int OutputCharacters { get; init; } = 32_000;
    /// <summary>Gets the native task identity used for diagnostics, never command authorization.</summary>
    public Guid TaskId { get; init; }
    /// <summary>Gets whether exact approval must describe a bounded background lifetime.</summary>
    public bool Background { get; init; }

    /// <summary>Validates and snapshots mutable argument collections before review. Returns: Frozen bounded request. Raises: ArgumentException for unsafe or excessive parameters.</summary>
    public CommandExecutionRequest Validated()
    {
        if (string.IsNullOrWhiteSpace(Executable) || !Path.IsPathFullyQualified(Executable) || !Path.IsPathFullyQualified(WorkingDirectory)
            || Executable.Contains('\0') || WorkingDirectory.Contains('\0') || Arguments is null || Arguments.Count > 128
            || Arguments.Any(argument => argument is null || argument.Length > 16_000 || argument.Contains('\0'))
            || Arguments.Sum(argument => (long)argument.Length + 3) + Executable.Length > 30_000)
        { throw new ArgumentException("Invalid or excessive structured process parameters."); }
        return this with { Executable = Path.GetFullPath(Executable), WorkingDirectory = Path.GetFullPath(WorkingDirectory),
            Arguments = Arguments.ToImmutableArray(), TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 1800), OutputCharacters = Math.Clamp(OutputCharacters, 1024, 128_000) };
    }
}

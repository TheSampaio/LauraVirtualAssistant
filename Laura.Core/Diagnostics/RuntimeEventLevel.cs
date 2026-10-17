using Microsoft.Extensions.Logging;

namespace Laura.Core.Diagnostics;

/// <summary>Assigns severity from native symbolic event codes without parsing private payloads.</summary>
public static class RuntimeEventLevel
{
    /// <summary>Returns the native event severity. Args: operation: Symbolic component; outcome: Symbolic result.</summary>
    public static LogLevel For(string operation, string outcome)
    {
        foreach (LogLevel level in new[] { LogLevel.Critical, LogLevel.Error, LogLevel.Warning, LogLevel.Debug, LogLevel.Trace })
        { if (operation.StartsWith("log." + level + ".", StringComparison.Ordinal)) { return level; } }
        if (outcome.Contains("fail", StringComparison.OrdinalIgnoreCase) || outcome == "nonzero_exit"
            || operation.EndsWith("_not_found", StringComparison.Ordinal) || operation.EndsWith("_failed", StringComparison.Ordinal)) { return LogLevel.Error; }
        if (outcome is "blocked" or "Denied" or "Blocked" or "Expired" or "timeout" or "cancelled") { return LogLevel.Warning; }
        if (operation.StartsWith("audio.chunk", StringComparison.Ordinal) || operation.StartsWith("provider.receive", StringComparison.Ordinal)
            || operation.StartsWith("provider.message_processing", StringComparison.Ordinal)) { return LogLevel.Debug; }
        return LogLevel.Information;
    }
}

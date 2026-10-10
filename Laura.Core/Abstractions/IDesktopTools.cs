using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>Typed desktop actions. Sensitive capabilities are confirmed by trusted UI inside the adapter.</summary>
public interface IDesktopTools
{
    /// <summary>Executes one bounded capability. There is no shell command, path, or confirmed argument.</summary>
    Task<string> ExecuteAsync(DesktopAction action, string? note, CancellationToken cancellationToken);
}

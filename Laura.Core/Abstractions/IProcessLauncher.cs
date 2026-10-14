namespace Laura.Core.Abstractions;

/// <summary>
/// Opens programs and external addresses on behalf of the assistant.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Opens an address in the system default application.
    ///
    /// Args:
    ///     uri: Address to open; accepts <c>http</c>, <c>https</c>, and
    ///     registered protocols such as <c>ms-settings:</c>.
    ///
    /// Returns:
    ///     <see langword="true"/> when the system accepted opening the address.
    /// </summary>
    bool TryOpen(Uri uri);

    /// <summary>
    /// Starts an executable.
    ///
    /// Args:
    ///     fileName: Executable name or path.
    ///     arguments: Command-line arguments, or <see langword="null"/>.
    ///
    /// Returns:
    ///     <see langword="true"/> when the process was started.
    /// </summary>
    bool TryStart(string fileName, string? arguments = null);

    /// <summary>Asynchronously opens a concrete destination after platform authorization.</summary>
    Task<bool> TryOpenAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(TryOpen(uri));

    /// <summary>Asynchronously launches exact parameters after platform authorization.</summary>
    Task<bool> TryStartAsync(string fileName, string? arguments = null, CancellationToken cancellationToken = default) => Task.FromResult(TryStart(fileName, arguments));
    /// <summary>Returns native creation evidence and optionally pins a learned executable hash at actual execution. Unsupported adapters fail closed for hash-pinned launches.</summary>
    async Task<ProcessLaunchResult> TryStartObservedAsync(string fileName, string? arguments = null, string? expectedHash = null, CancellationToken cancellationToken = default)
    {
        if (expectedHash is not null) { return new(false, fileName, Reason: "Hash-pinned launch is unavailable.") { StopFallback = true }; }
        return new(await TryStartAsync(fileName, arguments, cancellationToken).ConfigureAwait(false), fileName);
    }
}

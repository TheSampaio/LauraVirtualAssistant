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
}

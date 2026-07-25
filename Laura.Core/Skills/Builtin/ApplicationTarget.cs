namespace Laura.Core.Skills.Builtin;

/// <summary>
/// An application Laura knows how to open.
/// </summary>
/// <param name="AliasKey">
/// Language-file key holding the spoken aliases of the application, such as
/// <c>application.notepad</c>.
/// </param>
/// <param name="Target">Executable or address to launch.</param>
/// <param name="IsUri">
/// <see langword="true"/> when <paramref name="Target"/> is an address to open with
/// the default application rather than an executable.
/// </param>
/// <param name="Fallback">
/// Alternative executable, used when the main one is not present on the machine — the
/// Windows Terminal, for instance, is not on every installation.
/// </param>
public sealed record ApplicationTarget(
    string AliasKey,
    string Target,
    bool IsUri = false,
    string? Fallback = null);

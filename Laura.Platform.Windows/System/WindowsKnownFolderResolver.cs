using System.Runtime.InteropServices;
using Laura.Core.Abstractions;

namespace Laura.Platform.Windows.System;

/// <summary>Uses Windows known folders rather than guessing locations under the user profile.</summary>
public sealed partial class WindowsKnownFolderResolver : IKnownFolderResolver
{
    /// <inheritdoc />
    public string? Resolve(string alias) => alias.ToLowerInvariant() switch
    {
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "downloads" => Downloads(),
        _ => null,
    };

    /// <summary>Asks the shell for the current Downloads location, including OneDrive or custom redirection.</summary>
    private static string Downloads()
    {
        Guid id = new("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(in id, 0, 0, out nint path));
        try { return Marshal.PtrToStringUni(path) ?? throw new IOException("The Downloads known folder is unavailable."); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    /// <summary>Resolves a Windows shell-owned folder without creating or changing it.</summary>
    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}

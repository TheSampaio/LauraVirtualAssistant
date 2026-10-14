namespace Laura.App.Platforms.Windows;

/// <summary>Trusted native confirmation with the non-authorizing close button focused by default.</summary>
public static class WindowsConfirmation
{
    /// <summary>Serializes dialogs and closes a pending confirmation when its request is cancelled.</summary>
    public static async Task<bool> AskAsync(string message, string yes, string no, CancellationToken cancellationToken)
    {
        return await WindowsDialogs.ConfirmAsync("Laura", message, yes, no, cancellationToken).ConfigureAwait(false);
    }
}

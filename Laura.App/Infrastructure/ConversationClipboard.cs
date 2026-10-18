namespace Laura.App.Infrastructure;

/// <summary>Uses the native clipboard as plain text, preserving newlines, code, Unicode and URLs.</summary>
public static class ConversationClipboard
{
    /// <summary>Copies only the text explicitly chosen by a native user gesture.</summary>
    public static async Task<bool> CopyAsync(string text)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try { await Clipboard.Default.SetTextAsync(text).ConfigureAwait(true); return true; }
            catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 3)
            { await Task.Delay(20 << attempt).ConfigureAwait(true); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            { Laura.Core.Diagnostics.LauraDiagnostics.Record("clipboard.copy_failed", outcome: error.GetType().Name); return false; }
        }
        return false;
    }
}

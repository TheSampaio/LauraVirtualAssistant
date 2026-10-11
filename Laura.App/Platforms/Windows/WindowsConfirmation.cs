namespace Laura.App.Platforms.Windows;

/// <summary>Trusted native confirmation with the non-authorizing close button focused by default.</summary>
public static class WindowsConfirmation
{
    private static readonly SemaphoreSlim Dialogs = new(1, 1);
    /// <summary>Serializes dialogs and closes a pending confirmation when its request is cancelled.</summary>
    public static async Task<bool> AskAsync(string message, string yes, string no, CancellationToken cancellationToken)
    {
        await Dialogs.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var native = (Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!;
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = "Laura", Content = message, PrimaryButtonText = yes, CloseButtonText = no,
                    DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
                    XamlRoot = native.Content.XamlRoot, RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                };
                using CancellationTokenRegistration cancelled = cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(dialog.Hide));
                var result = await dialog.ShowAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
            }).ConfigureAwait(false);
        }
        finally { Dialogs.Release(); }
    }
}

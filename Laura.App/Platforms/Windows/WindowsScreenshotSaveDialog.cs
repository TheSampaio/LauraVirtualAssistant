using Laura.Core.Abstractions;
using Laura.Core.Permissions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace Laura.App.Platforms.Windows;

/// <summary>Asynchronous Laura save form: native folder browsing and explicit path/format selection without touching a destination file.</summary>
internal static class WindowsScreenshotSaveDialog
{
    private static readonly string[] Formats = ["PNG", "JPEG"];
    internal static Task<string?> ChooseAsync(nint owner, IUiLocalizer ui, CancellationToken token) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        string T(string key) => ui.Get("ui.screenshot." + key);
        var folder = new TextBox { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), MaxLength = 3000 };
        if (folder.Text.Length == 0) { folder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        var name = new TextBox { Text = "Screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", global::System.Globalization.CultureInfo.InvariantCulture), MaxLength = 180 };
        var format = new ComboBox { ItemsSource = Formats, SelectedIndex = 0, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        var status = WindowsDialogs.Text("", 12, true);
        AutomationProperties.SetName(folder, T("folder")); AutomationProperties.SetName(name, T("fileName")); AutomationProperties.SetName(format, T("format"));
        var browse = WindowsDialogs.Button(T("browse"), () => { });
        browse.Click += async (_, _) =>
        {
            browse.IsEnabled = false;
            try
            {
                var picker = new global::Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
                var selected = await picker.PickSingleFolderAsync().AsTask(token).ConfigureAwait(true);
                if (selected is not null && !token.IsCancellationRequested) { folder.Text = selected.Path; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException) { status.Text = T("error"); }
            finally { browse.IsEnabled = true; }
        };
        var fields = new StackPanel { Spacing = 12 };
        foreach (var field in new (string Title, FrameworkElement Control)[] { (T("folder"), folder), (T("fileName"), name), (T("format"), format) }) { fields.Children.Add(WindowsDialogs.Text(field.Title, 12, true)); fields.Children.Add(field.Control); }
        fields.Children.Insert(2, browse); fields.Children.Add(status);
        string? path = null;
        bool accepted = await WindowsDialogs.ShowAsync(T("save"), T("saveHelp"), T("save"), T("cancel"), fields, token, () =>
        {
            try
            {
                string fileName = name.Text.Trim();
                if (fileName.Length == 0 || fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !Path.IsPathFullyQualified(folder.Text)) { throw new ArgumentException("Invalid file name or folder."); }
                if (new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase)) { fileName = Path.GetFileNameWithoutExtension(fileName); }
                path = Path.GetFullPath(Path.Combine(folder.Text, fileName + (format.SelectedIndex == 1 ? ".jpg" : ".png")));
                PermissionPolicy.ValidateResources(new(PermissionOperation.ScreenshotExport, "Screenshot save selection", "", [path]), []);
                if (!Directory.Exists(Path.GetDirectoryName(path))) { throw new IOException("Choose an existing folder."); }
                return true;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or PermissionDeniedException) { path = null; status.Text = T("invalidDestination"); return false; }
        }, height: 640).ConfigureAwait(true);
        if (!accepted || path is null) { return null; }
        if (File.Exists(path) && !await WindowsDialogs.ConfirmAsync(T("save"), T("overwrite"), T("save"), T("cancel"), token).ConfigureAwait(true)) { return null; }
        return path;
    });
}

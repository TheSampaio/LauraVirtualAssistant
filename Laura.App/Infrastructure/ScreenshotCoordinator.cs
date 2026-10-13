using System.Security.Cryptography;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Screenshots;
using Laura.Core.Permissions;
using Laura.Core.Brain;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using NativeClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Laura.App.Infrastructure;

/// <summary>Owns one bounded capture/edit session and connects native actions to existing authorization, conversation and Brain services.</summary>
public sealed class ScreenshotCoordinator(PermissionManager permissions, ISettingsService settings, IUiLocalizer ui, IServiceProvider services) : IDisposable
{
    private bool _disposed;
    private ScreenshotEditorWindow? _editor;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _capture = new(1, 1), _actions = new(1, 1);
    /// <summary>Gets whether an editable screenshot is currently available.</summary>
    public bool HasCapture => _editor is not null;
    /// <summary>Gets actual registration failures, without silently choosing new chords.</summary>
    public IReadOnlyList<string> ShortcutErrors { get; private set; } = [];
    /// <summary>Reports changes to shortcut diagnostics for the existing settings UI.</summary>
    public event EventHandler? ShortcutStatusChanged;
    internal void SetShortcutErrors(IReadOnlyList<string> errors) { ShortcutErrors = errors; ShortcutStatusChanged?.Invoke(this, EventArgs.Empty); }
    private string T(string key) => ui.Get("ui.screenshot." + key);
    /// <summary>Starts an explicitly requested capture. Only native HUD/shortcut gestures set userInitiated; model fields never do.</summary>
    public async Task<bool> CaptureAsync(ScreenshotMode mode, bool userInitiated = false, CancellationToken cancellationToken = default)
    {
        if (_disposed) { return false; }
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        if (!await _capture.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { return false; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        bool allDisplays = settings.Current.Screenshots.AllDisplays;
        try
        {
            return await permissions.ExecuteAsync(new(PermissionOperation.LocalCapture, userInitiated ? "Screenshot native gesture" : "Screenshot assistant tool", T("capture." + mode),
                parameters: mode + ":" + allDisplays, userInitiated: userInitiated), async token =>
                {
                    await MainThread.InvokeOnMainThreadAsync(() => { _editor?.Close(); _editor = null; }).ConfigureAwait(false);
                    ScreenshotFrame? result = null;
                    try
                    {
                        ScreenshotBounds bounds = WindowsScreenCapture.DesktopBounds(mode != ScreenshotMode.FullScreen || allDisplays);
                        if (mode == ScreenshotMode.FullScreen) { result = await WindowsScreenCapture.CaptureFrameAsync(bounds, token).ConfigureAwait(false); }
                        else
                        {
                            using var desktop = await WindowsScreenCapture.CaptureFrameAsync(bounds, token).ConfigureAwait(false);
                            var selection = await MainThread.InvokeOnMainThreadAsync(() => ScreenshotSelectionWindow.ChooseAsync(desktop, mode, ui, token)).ConfigureAwait(false);
                            if (selection is null) { return false; }
                            result = selection.Window is { } window ? await MainThread.InvokeOnMainThreadAsync(() => WindowsWindowCapture.CaptureAsync(window, token)).ConfigureAwait(false) : desktop.Crop(selection.Bounds);
                        }
                        ScreenshotFrame captured = result;
                        await MainThread.InvokeOnMainThreadAsync(() => { token.ThrowIfCancellationRequested(); Open(captured); }).ConfigureAwait(false); result = null; return true;
                    }
                    finally { result?.Dispose(); }
                }, linked.Token).ConfigureAwait(false);
        }
        catch (PermissionDeniedException) { return false; }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException or IOException or ArgumentException)
        { services.GetRequiredService<LauraNotifications>().Publish("LAURA Screenshot", T("error") + " · " + error.GetType().Name, LauraNotificationKind.Error); return false; }
        finally { _capture.Release(); }
    }
    private void Open(ScreenshotFrame frame)
    {
        var editor = new ScreenshotEditorWindow(new(frame), ui, action => PerformAsync(action, true));
        _editor = editor; editor.Closed += (_, _) => { if (ReferenceEquals(_editor, editor)) { _editor = null; } };
    }
    /// <summary>Copies/exports/stores the current image through the same native session; cloud analysis stays in the normal conversation.</summary>
    public async Task<bool> PerformAsync(string action, bool userInitiated = false, string? topic = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _actions.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { throw new InvalidOperationException("A screenshot action is already running. Wait for it to finish."); }
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var editor = _editor ?? throw new InvalidOperationException(T("noCapture"));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, editor.Lifetime, _lifetime.Token); var token = linked.Token;
                editor.SetBusy(true);
                try
                {
                    if (action == "cancel") { editor.Close(); return true; }
                    if (action == "save")
                    {
                        string? selectedPath = await WindowsScreenshotSaveDialog.ChooseAsync(WinRT.Interop.WindowNative.GetWindowHandle(editor.NativeWindow), ui, token).ConfigureAwait(true); if (selectedPath is null) { return false; }
                        string extension = Path.GetExtension(selectedPath);
                        bool jpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
                        if (!jpeg && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) { throw new IOException(T("formatError")); }
                        byte[] encoded = await editor.ExportAsync(jpeg, token).ConfigureAwait(true);
                        await permissions.ExecuteAsync(new(PermissionOperation.ScreenshotExport, "Screenshot save", selectedPath, [Path.GetFullPath(selectedPath)], Digest(encoded), userInitiated: userInitiated),
                            async approved => { await SaveAtomicallyAsync(selectedPath, encoded, approved).ConfigureAwait(false); return true; }, token).ConfigureAwait(true);
                        editor.SetStatus(T("saved")); return true;
                    }
                    byte[] png = await editor.ExportAsync(false, token).ConfigureAwait(true);
                    if (action == "copy")
                    {
                        await permissions.ExecuteAsync(new(PermissionOperation.ClipboardWrite, "Screenshot clipboard", T("copy"), parameters: Digest(png), userInitiated: userInitiated), async approved =>
                        { await CopyAsync(png, approved).ConfigureAwait(true); return true; }, token).ConfigureAwait(true); editor.SetStatus(T("copied"));
                    }
                    else if (action == "analyze")
                    {
                        var live = services.GetRequiredService<GeminiLiveClient>(); if (!live.SupportsImages) { editor.SetStatus(T("unsupported")); return false; }
                        string? question = await WindowsDialogs.PromptAsync(T("analyze"), T("question"), T("defaultQuestion"), token: token).ConfigureAwait(true); if (string.IsNullOrWhiteSpace(question)) { return false; }
                        services.GetRequiredService<Laura.App.Shell.MauiShellController>().RevealForConfirmation();
                        await services.GetRequiredService<IAssistantEngine>().SubmitVisualCommandAsync(question, png, token).ConfigureAwait(true); editor.SetStatus(T("conversation"));
                    }
                    else if (action == "brain")
                    {
                        if (string.IsNullOrWhiteSpace(topic)) { topic = await WindowsDialogs.PromptAsync(T("brain"), T("topic"), token: token).ConfigureAwait(true); } if (string.IsNullOrWhiteSpace(topic)) { return false; }
                        BrainService brain = services.GetRequiredService<BrainService>(); await brain.RefreshAsync(cancellationToken: token).ConfigureAwait(true);
                        var matches = brain.Snapshot.Notes.Where(note => note.IsKnowledge && (note.Title.Equals(topic, StringComparison.OrdinalIgnoreCase) || note.Aliases.Contains(topic, StringComparer.OrdinalIgnoreCase))).ToArray();
                        string? id = matches.Length == 1 ? matches[0].Id : null;
                        if (matches.Length > 1)
                        {
                            string[] choices = matches.Select(note => note.Title + " · " + note.Path).ToArray(); string? choice = await WindowsDialogs.ChooseAsync(T("chooseNode"), T("cancel"), null, choices).ConfigureAwait(true);
                            if (choice is null) { return false; } id = matches[Array.IndexOf(choices, choice)].Id;
                        }
                        string selectedTopic = topic;
                        string selectedVault = brain.Location;
                        await permissions.ExecuteAsync(new(PermissionOperation.BrainAttachment, "Screenshot Brain attachment", selectedTopic + "\n" + selectedVault,
                            parameters: Digest(png) + ":" + selectedTopic + ":" + id + ":" + selectedVault, destination: PermissionPolicy.IsNetworkPath(selectedVault) ? selectedVault : "", userInitiated: userInitiated), approved => brain.AttachVisualAsync(png, selectedTopic, id, selectedVault, approved), token).ConfigureAwait(true);
                        editor.SetStatus(T("brainSaved"));
                    }
                    else { throw new ArgumentException("Unknown screenshot action.", nameof(action)); }
                    return true;
                }
                finally { if (_editor == editor) { editor.SetBusy(false); } }
            }).ConfigureAwait(false);
        }
        finally { _actions.Release(); }
    }
    /// <summary>Returns an exact locally rendered PNG to the current assistant tool flow; the caller must authorize its actual cloud transmission.</summary>
    public async Task<byte[]> CurrentPngAsync(CancellationToken token) => (await CurrentSnapshotAsync(token).ConfigureAwait(false)).Png;
    internal async Task<(byte[] Png, CancellationToken Lifetime)> CurrentSnapshotAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _actions.WaitAsync(0, token).ConfigureAwait(false)) { throw new InvalidOperationException("A screenshot action is already running."); }
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var editor = _editor ?? throw new InvalidOperationException(T("noCapture"));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, editor.Lifetime, _lifetime.Token);
                editor.SetBusy(true);
                try { return (await editor.ExportAsync(false, linked.Token).ConfigureAwait(true), editor.Lifetime); }
                finally { if (_editor == editor) { editor.SetBusy(false); } }
            }).ConfigureAwait(false);
        }
        finally { _actions.Release(); }
    }
    private static string Digest(byte[] image) => Convert.ToHexString(SHA256.HashData(image));
    private static async Task SaveAtomicallyAsync(string path, byte[] image, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { await stream.WriteAsync(image, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); PermissionPolicy.ValidateResources(new(PermissionOperation.ScreenshotExport, "Screenshot export", "", [path, temporary]), []);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    private static async Task CopyAsync(byte[] png, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream)) { writer.WriteBytes(png); await writer.StoreAsync().AsTask(token).ConfigureAwait(true); writer.DetachStream(); } stream.Seek(0);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage { RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy };
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream)); NativeClipboard.SetContent(package); NativeClipboard.Flush(); return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 2) { await Task.Delay(80, token).ConfigureAwait(true); }
        }
    }
    internal ScreenshotEditorWindow EditorForSmoke => _editor ?? throw new InvalidOperationException("No test editor.");
    internal void OpenForSmoke(ScreenshotFrame frame)
    {
        if (!Environment.GetCommandLineArgs().Contains("--screenshot-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated screenshot diagnostics only."); } Open(frame);
    }
    internal async Task SaveForSmokeAsync(string path, bool jpeg)
    {
        if (!Environment.GetCommandLineArgs().Contains("--screenshot-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated screenshot diagnostics only."); }
        byte[] encoded = await EditorForSmoke.ExportAsync(jpeg, _lifetime.Token).ConfigureAwait(true);
        await permissions.ExecuteAsync(new(PermissionOperation.ScreenshotExport, "Screenshot diagnostic", path, [Path.GetFullPath(path)], Digest(encoded), userInitiated: true),
            async token => { await SaveAtomicallyAsync(path, encoded, token).ConfigureAwait(false); return true; }, _lifetime.Token).ConfigureAwait(true);
    }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) { return; } _disposed = true; _lifetime.Cancel(); _editor?.Close(); _editor = null; }
    /// <summary>Cancels and drains active selection/export work before releasing synchronization resources on application exit.</summary>
    public async Task StopAsync()
    {
        Dispose(); await _capture.WaitAsync().ConfigureAwait(true); await _actions.WaitAsync().ConfigureAwait(true);
        _capture.Release(); _actions.Release(); _capture.Dispose(); _actions.Dispose(); _lifetime.Dispose();
    }
}

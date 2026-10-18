using System.Runtime.InteropServices;
using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in integration tests in the actual MAUI/WinUI process, with isolated data and harmless file fixtures.</summary>
public static class PermissionSmokeRunner
{
    /// <summary>Verifies real tool dispatch, independent windows, UI automation peers and window lifecycle without cloud data or microphone.</summary>
    public static async Task RunAsync(IServiceProvider services, Window main, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var settings = services.GetRequiredService<ISettingsService>();
        var permissions = services.GetRequiredService<PermissionManager>();
        var center = services.GetRequiredService<WindowsApprovalCenter>();
        var tools = services.GetRequiredService<IAssistantTools>();
        var native = (Microsoft.UI.Xaml.Window)main.Handler!.PlatformView!;
        var shell = services.GetRequiredService<Laura.App.Shell.MauiShellController>();
        string targets = Path.Combine(directory, "Targets"); Directory.CreateDirectory(targets);
        if (!Environment.GetCommandLineArgs().Contains("--storage", StringComparer.Ordinal)
            || PermissionPolicy.Within(targets, services.GetRequiredService<AssistantStorage>().Directory))
        { throw new InvalidOperationException("Pass isolated --storage outside the synthetic Targets directory."); }
        var results = new List<string>();
        try
        {
            await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, MicrophoneMuted = true,
                Permissions = new() { Voice = ApprovalVoiceMode.Never, TimeoutSeconds = 10 } }).ConfigureAwait(true);
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(native);
            foreach (string state in new[] { "visible", "hidden", "minimized" })
            {
                shell.RevealForConfirmation();
                if (state == "hidden") { ShowWindow(handle, 0); }
                else if (state == "minimized") { ShowWindow(handle, 6); }
                string path = Path.Combine(targets, state + ".md");
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path, content = "# Synthetic permission integration\n" }));
                Task<AssistantToolResult> pending = tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None);
                await WaitPendingAsync(permissions).ConfigureAwait(true); await Task.Delay(250).ConfigureAwait(true);
                var independent = center.NativeWindowForSmoke;
                if (!independent.AppWindow.IsVisible || independent.Content.XamlRoot == native.Content.XamlRoot
                    || independent.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { IsAlwaysOnTop: true })
                { throw new InvalidOperationException("Approval UI is not an independent visible non-topmost window."); }
                if (state == "hidden" && IsWindowVisible(handle) || state == "minimized" && !IsIconic(handle))
                { throw new InvalidOperationException("Background approval altered primary-window visibility."); }
                center.Show(); await Task.Delay(100).ConfigureAwait(true);
                await CaptureAsync(independent, Path.Combine(directory, "approval-" + state + ".png")).ConfigureAwait(true);
                center.InvokeForSmoke("once");
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                if (result.Failed || !File.Exists(path) || await File.ReadAllTextAsync(path).ConfigureAwait(true) != "# Synthetic permission integration\n")
                { throw new InvalidOperationException("Approved tool failed to dispatch the actual write."); }
                results.Add(state + ": independent UI, actual approval button, real file write passed.");
            }
            string deniedPath = Path.Combine(targets, "denied.md");
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = deniedPath, content = "must not write" })))
            {
                var task = tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None);
                await WaitPendingAsync(permissions).ConfigureAwait(true); center.Show(); await Task.Delay(150).ConfigureAwait(true); center.InvokeForSmoke("deny"); await task.ConfigureAwait(true);
                if (File.Exists(deniedPath)) { throw new InvalidOperationException("Denied tool executed."); }
                results.Add("Denial through native button prevented actual file write.");
            }
            using (var cancellation = new CancellationTokenSource())
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = Path.Combine(targets, "cancelled.md"), content = "must not write" })))
            {
                var task = tools.ExecuteAsync("file_control", arguments.RootElement, cancellation.Token);
                await WaitPendingAsync(permissions).ConfigureAwait(true); Guid id = permissions.PendingRequests.Single().Id; cancellation.Cancel();
                try { await task.ConfigureAwait(true); throw new InvalidOperationException("Cancellation was not propagated."); } catch (OperationCanceledException) { }
                if (permissions.Decide(id, ApprovalChoice.Once) || File.Exists(Path.Combine(targets, "cancelled.md"))) { throw new InvalidOperationException("Late cancellation approval dispatched."); }
                results.Add("Cancelled actual tool cannot be revived by late approval.");
            }
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = Path.Combine(targets, "expired.md"), content = "must not write" })))
            {
                var task = tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None); await WaitPendingAsync(permissions).ConfigureAwait(true);
                Guid id = permissions.PendingRequests.Single().Id; await task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                if (permissions.Decide(id, ApprovalChoice.Once) || File.Exists(Path.Combine(targets, "expired.md"))) { throw new InvalidOperationException("Expired tool dispatched."); }
                results.Add("Expired actual tool cannot be revived by late approval.");
            }
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Security", "permissions.json"), content = "malicious policy" })))
            { await tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None).ConfigureAwait(true); if (permissions.PendingRequests.Count != 0) { throw new InvalidOperationException("Tool offered an approval for its security storage."); } results.Add("Generic model file tools cannot alter application/security storage."); }
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Automatic } }).ConfigureAwait(true);
            services.GetRequiredService<Laura.Core.Abstractions.IConversationMemory>().SetWorkingContext(
                new Laura.Core.Conversation.ConversationWorkingContext(Guid.NewGuid(), "Create a code file in Laura Workspace."));
            string workspace = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace");
            Directory.CreateDirectory(workspace);
            string automaticFile = Path.Combine(workspace, "AutomaticProgram.cs");
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = automaticFile, content = "class AutomaticFixture {}" })))
            {
                AssistantToolResult result = await tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                if (result.Failed || !File.Exists(automaticFile) || permissions.PendingRequests.Count != 0
                    || permissions.History[^1].Outcome != ApprovalOutcome.Automatic) { throw new InvalidOperationException("Automatic task write required review or failed to create code."); }
                results.Add("Automatic mode created a real code file in the assigned Workspace without a pending approval.");
            }
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.FullAccess } }).ConfigureAwait(true);
            if (permissions.CurrentMode != ApprovalMode.Ask) { throw new InvalidOperationException("Saved full access became active without a native owner gesture."); }
            permissions.ActivateFullAccess();
            shell.RevealForConfirmation(); await Task.Delay(150).ConfigureAwait(true);
            await CaptureAsync(native, Path.Combine(directory, "full-access-hud.png")).ConfigureAwait(true);
            string fullAccessFile = Path.Combine(targets, "full-access.md");
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = fullAccessFile, content = "# Native full-access fixture" })))
            {
                AssistantToolResult result = await tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                if (result.Failed || !File.Exists(fullAccessFile) || permissions.PendingRequests.Count != 0
                    || !permissions.History[^1].Reason.Contains("full_access", StringComparison.Ordinal)) { throw new InvalidOperationException("Activated full access did not dispatch the real tool."); }
                results.Add("Session-activated full access created a real external fixture file without per-action approval.");
            }
            permissions.DeactivateFullAccess();
            string revokedFile = Path.Combine(targets, "full-access-revoked.md");
            using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "write", path = revokedFile, content = "must not write" })))
            {
                Task<AssistantToolResult> pending = tools.ExecuteAsync("file_control", arguments.RootElement, CancellationToken.None);
                await WaitPendingAsync(permissions).ConfigureAwait(true);
                permissions.Decide(permissions.PendingRequests.Single().Id, ApprovalChoice.Deny);
                await pending.ConfigureAwait(true);
                if (File.Exists(revokedFile)) { throw new InvalidOperationException("Revoked full access dispatched an unapproved write."); }
                results.Add("Turning off full access immediately restored native review and denial.");
            }
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Ask } }).ConfigureAwait(true);
            shell.RevealForConfirmation();
            var setup = services.GetRequiredService<Views.SetupPage>();
            await ((NavigationPage)main.Page!).PushAsync(setup, false).ConfigureAwait(true); setup.SelectTab(4); await Task.Delay(250).ConfigureAwait(true);
            setup.VerifySettingsForSmoke();
            await CaptureAsync(native, Path.Combine(directory, "security-settings.png")).ConfigureAwait(true);
            results.Add("Native Security tab rendered; main UI remained responsive during pending operations.");
            await setup.SaveSettingsForPermissionSmokeAsync().ConfigureAwait(true);
            results.Add("Security preferences saved through the real settings lifecycle, including when no Gemini key is configured.");
            if (Environment.GetCommandLineArgs().Contains("--permissions-live-smoke", StringComparer.Ordinal))
            {
                var live = services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>();
                if (!live.IsAvailable) { throw new InvalidOperationException("VALIDATION BLOCKED: no configured Gemini credential is available for the required live voice integration."); }
                await settings.UpdateAsync(settings.Current with { Voice = settings.Current.Voice with { Volume = 0 } }).ConfigureAwait(true);
                using var voiceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                string announcement = services.GetRequiredService<IUiLocalizer>().GetForCulture(settings.Current.Culture, "ui.permissions.announcement");
                await live.VerifyApprovalVoiceForSmokeAsync(announcement, voiceTimeout.Token).ConfigureAwait(true);
                results.Add("Live Gemini produced PCM and transcripts for two serialized generic announcements while the conversation gate was occupied. Playback volume was zero; no microphone or personal context was sent.");
            }
            else { results.Add("Live cloud TTS was not executed in this offline diagnostic."); }
            results.Add("OS notification suppression, multiple monitors, audible voice output and screen-reader interaction require separate manual verification; they were not asserted by this isolated run.");
            await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), results.Prepend("PASS: isolated permission integration")).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), results.Append("FAIL: " + error.ToString())).ConfigureAwait(true);
        }
        finally { shell.Shutdown(); }
    }
    private static async Task WaitPendingAsync(PermissionManager permissions)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (permissions.PendingRequests.Count == 0) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
    }
    private static async Task CaptureAsync(Microsoft.UI.Xaml.Window window, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(window.Content); var buffer = await bitmap.GetPixelsAsync();
        byte[] pixels = new byte[buffer.Length]; using (var reader = DataReader.FromBuffer(buffer)) { reader.ReadBytes(pixels); }
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0); await using var input = stream.AsStreamForRead(); await using var output = File.Create(path); await input.CopyToAsync(output).ConfigureAwait(true);
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
}

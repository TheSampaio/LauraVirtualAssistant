using System.Runtime.InteropServices;
using System.Text;
using System.IO.Compression;
using Laura.App.Platforms.Windows;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Diagnostics;
using Laura.Core.Screenshots;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Isolated native validation without capturing the desktop, microphone, or sending personal content to a provider.</summary>
internal static class ChatDiagnosticsSmokeRunner
{
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
        nint owner = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var settings = services.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(settings.Current with { Screenshots = new ScreenshotOptions(), GreetOnStartup = false, AnnounceHourly = false, ClapWakeEnabled = false }).ConfigureAwait(true);
        var capture = services.GetRequiredService<ScreenshotCoordinator>(); var ui = services.GetRequiredService<IUiLocalizer>(); var notices = services.GetRequiredService<LauraNotifications>();
        var shortcuts = new ScreenshotOptions();
        if (!ScreenshotOptions.TryParse(shortcuts.RegionShortcut, out uint modifiers, out uint key)) { throw new InvalidOperationException("Invalid recommended shortcut."); }
        bool reserved = RegisterHotKey(owner, 780, modifiers, key);
        try
        {
            if (!reserved) { throw new InvalidOperationException("Recommended region shortcut is occupied in this environment; no registration was replaced."); }
            using (var conflicted = new WindowsScreenshotShortcuts(owner, settings, capture, ui, notices))
            { if (capture.ShortcutErrors.Count != 1) { throw new InvalidOperationException("A native shortcut collision must be reported and not replaced."); } }
        }
        finally { if (reserved) { UnregisterHotKey(owner, 780); } }
        using (var available = new WindowsScreenshotShortcuts(owner, settings, capture, ui, notices))
        { if (capture.ShortcutErrors.Count != 0) { throw new InvalidOperationException("Recommended shortcuts must register after the competing registration is released."); } }
        var page = services.GetRequiredService<MainPage>(); await page.VerifyChatUxForSmokeAsync(directory).ConfigureAwait(true);
        var snapshot = await services.GetRequiredService<RuntimeJournal>().SnapshotAsync().ConfigureAwait(true);
        await using (var output = File.Create(Path.Combine(directory, "diagnostic.zip"))) { await DiagnosticExport.WriteAsync(output, snapshot, "2.0.0").ConfigureAwait(true); }
        using (var zip = ZipFile.OpenRead(Path.Combine(directory, "diagnostic.zip")))
        { if (zip.GetEntry("report.md") is null || zip.GetEntry("events.jsonl") is null || zip.GetEntry("summary.json") is null) { throw new InvalidOperationException("Incomplete diagnostic bundle."); } }
        await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "chat-native.png")).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Native RegisterHotKey conflict and release verified without registering PrtSc. Chat selection/copy/paste, real layout following, manual-position suspension, streaming growth, transcript reset and private technical ZIP validated in isolated storage.", new UTF8Encoding(false)).ConfigureAwait(true);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint owner, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint owner, int id);
}

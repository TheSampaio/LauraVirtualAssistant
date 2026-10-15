using System.Runtime.InteropServices;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Screenshots;

namespace Laura.App.Platforms.Windows;

/// <summary>Registers configurable global capture chords without replacing unavailable Windows/application registrations.</summary>
public sealed class WindowsScreenshotShortcuts : IDisposable
{
    private readonly nint _handle;
    private readonly ISettingsService _settings;
    private readonly ScreenshotCoordinator _screenshots;
    private readonly IUiLocalizer _ui;
    private readonly LauraNotifications _notifications;
    private readonly SubclassProcedure _procedure;
    private readonly HashSet<int> _registered = [];
    private ScreenshotOptions? _applied;
    private bool _disposed;
    /// <summary>Attaches a separate subclass to the existing tray/HUD handle.</summary>
    public WindowsScreenshotShortcuts(nint handle, ISettingsService settings, ScreenshotCoordinator screenshots, IUiLocalizer ui, LauraNotifications notifications)
    {
        _handle = handle; _settings = settings; _screenshots = screenshots; _ui = ui; _notifications = notifications; _procedure = Procedure;
        if (!SetWindowSubclass(handle, _procedure, 3, 0)) { throw new InvalidOperationException("Screenshot shortcut hook unavailable."); }
        settings.Changed += Changed; Apply();
    }
    private void Changed(object? sender, LauraSettings settings) => MainThread.BeginInvokeOnMainThread(Apply);
    private void Apply()
    {
        if (_disposed || _applied == _settings.Current.Screenshots) { return; } _applied = _settings.Current.Screenshots;
        foreach (int id in _registered) { UnregisterHotKey(_handle, id); } _registered.Clear();
        var errors = new List<string>();
        if (_applied.ShortcutsEnabled)
        {
            string[] chords = [_applied.RegionShortcut, _applied.WindowShortcut, _applied.FullScreenShortcut];
            var seen = new HashSet<(uint Modifiers, uint Key)>();
            for (int index = 0; index < chords.Length; index++)
            {
                bool parsed = ScreenshotOptions.TryParse(chords[index], out uint modifiers, out uint key);
                bool unique = parsed && seen.Add((modifiers, key));
                bool registered = unique && RegisterHotKey(_handle, 100 + index, modifiers, key);
                int error = unique && !registered ? Marshal.GetLastWin32Error() : 0;
                Laura.Core.Diagnostics.LauraDiagnostics.Record("screenshot.shortcut." + (ScreenshotMode)index, amount: error, outcome: registered ? "registered" : unique ? "unavailable" : "invalid_or_duplicate");
                if (!registered) { errors.Add(_ui.Get("ui.screenshot.shortcutConflict") + ": " + chords[index] + (error == 0 ? "" : " (" + error + ")")); }
                else { _registered.Add(100 + index); }
            }
        }
        _screenshots.SetShortcutErrors(errors);
        if (errors.Count > 0) { _notifications.Publish("LAURA Screenshot", string.Join("\n", errors), LauraNotificationKind.Warning); }
    }
    private nint Procedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x312 && _registered.Contains((int)wParam)) { _ = RunAsync((ScreenshotMode)((int)wParam - 100)); return 0; }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private async Task RunAsync(ScreenshotMode mode) { try { await _screenshots.CaptureAsync(mode, true).ConfigureAwait(true); } catch (OperationCanceledException) { } }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) { return; } _disposed = true; _settings.Changed -= Changed; foreach (int id in _registered) { UnregisterHotKey(_handle, id); } _registered.Clear(); RemoveWindowSubclass(_handle, _procedure, 3); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
}

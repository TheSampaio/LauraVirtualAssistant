using System.Runtime.InteropServices;
using Laura.App.Views;
using Laura.Core.Conversation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;

namespace Laura.App.Platforms.Windows;

/// <summary>Supports global hold-to-talk and focused-window mute, interrupt and fullscreen shortcuts.</summary>
public sealed class WindowsShortcutHost : IDisposable
{
    private readonly Microsoft.UI.Xaml.Window _native;
    private readonly nint _handle;
    private readonly MainPage _page;
    private readonly IDispatcherTimer _timer;
    private readonly ShortcutStateTracker _state = new();
    /// <summary>Polls both key press and release, including when another app has focus.</summary>
    public WindowsShortcutHost(Window window, MainPage page)
    {
        _page = page; _native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(_native);
        _timer = window.Dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromMilliseconds(33); _timer.Tick += Tick; _timer.Start();
    }
    /// <summary>Toggles a true native fullscreen presenter and restores the normal window on exit.</summary>
    public void ToggleFullscreen() => WindowsWindowTheme.ToggleFullscreen(_native);
    /// <summary>Dispatches only debounced transitions; plain M is ignored while editing text.</summary>
    private void Tick(object? sender, EventArgs args)
    {
        bool foreground = GetForegroundWindow() == _handle;
        object? focus = _native.Content.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
        bool editing = focus is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.RichEditBox or Microsoft.UI.Xaml.Controls.PasswordBox;
        bool control = Down(0x11), alt = Down(0x12), shift = Down(0x10);
        ShortcutChanges changes = _state.Update(Environment.TickCount64, control && Down(0x20) && !alt && !shift,
            foreground, editing, Down(0x1B), Down(0x4D) && !control && !alt, Down(0x7A) && !control && !alt && !shift);
        if (changes.PushToTalk is { } held) { _ = RunAsync(() => _page.SetPushToTalkAsync(held)); }
        if (changes.Interrupt) { _ = RunAsync(_page.InterruptAsync); }
        if (changes.ToggleMute) { _ = RunAsync(_page.ToggleMicrophoneAsync); }
        if (changes.ToggleFullscreen) { ToggleFullscreen(); }
    }
    /// <summary>Contains asynchronous input failures without terminating the native message loop.</summary>
    private static async Task RunAsync(Func<Task> action) { try { await action().ConfigureAwait(true); } catch (Exception) { } }
    /// <summary>Reads the current physical key state rather than repeat messages.</summary>
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    /// <inheritdoc />
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; GC.SuppressFinalize(this); }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}

using Laura.App.Theming;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Screenshots;

namespace Laura.App.Views;

/// <summary>Screenshot preferences embedded in the existing Preferences tab.</summary>
public sealed class ScreenshotSettingsView : ContentView
{
    private readonly Switch _enabled = new() { OnColor = Theme.Accent }, _all = new() { OnColor = Theme.Accent };
    private readonly Entry _region = Theme.Entry(), _window = Theme.Entry(), _screen = Theme.Entry();
    private readonly string _duplicateMessage;
    /// <summary>Creates native settings fields and exposes registration failures for correction.</summary>
    public ScreenshotSettingsView(ISettingsService settings, IUiLocalizer ui, ScreenshotCoordinator screenshots)
    {
        string T(string key) => ui.Get("ui.screenshot." + key);
        _duplicateMessage = T("duplicate");
        var status = Theme.Label(string.Join("\n", screenshots.ShortcutErrors), 12, Theme.Accent);
        void Refresh(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() => status.Text = string.Join("\n", screenshots.ShortcutErrors));
        Loaded += (_, _) => { screenshots.ShortcutStatusChanged -= Refresh; screenshots.ShortcutStatusChanged += Refresh; Refresh(null, EventArgs.Empty); };
        Unloaded += (_, _) => screenshots.ShortcutStatusChanged -= Refresh;
        Content = Theme.Stack(Theme.Caption("LAURA Screenshot"), Theme.Label(T("shortcutsHelp"), 12, Theme.Muted),
            Field(T("shortcuts"), _enabled), Field(T("capture.Region"), _region), Field(T("capture.Window"), _window), Field(T("capture.FullScreen"), _screen), Field(T("allDisplays"), _all),
            Theme.ActionButton(T("recommended"), "scan", () => { Reload(new ScreenshotOptions { ShortcutsEnabled = _enabled.IsToggled, AllDisplays = _all.IsToggled }); return Task.CompletedTask; }), status);
        Reload(settings.Current.Screenshots);
    }
    /// <summary>Gets validated chord settings for the existing Save button.</summary>
    public ScreenshotOptions Options
    {
        get
        {
            if (_enabled.IsToggled && new[] { _region.Text, _window.Text, _screen.Text }.Any(chord => chord is null || !ScreenshotOptions.TryParse(chord, out _, out _))) { throw new ArgumentException("Use PrtSc, modifier+letter, or F1–F11 chords. F12 is reserved."); }
            if (_enabled.IsToggled && new[] { _region.Text, _window.Text, _screen.Text }.Select(chord => { if (!ScreenshotOptions.TryParse(chord!, out uint modifiers, out uint key)) { throw new ArgumentException(_duplicateMessage); } return (modifiers, key); }).Distinct().Count() != 3)
            { throw new ArgumentException(_duplicateMessage); }
            return new() { ShortcutsEnabled = _enabled.IsToggled, AllDisplays = _all.IsToggled, RegionShortcut = _region.Text ?? "", WindowShortcut = _window.Text ?? "", FullScreenShortcut = _screen.Text ?? "" };
        }
    }
    /// <summary>Restores persisted settings when reopening the cached page.</summary>
    public void Reload(ScreenshotOptions options) { _enabled.IsToggled = options.ShortcutsEnabled; _all.IsToggled = options.AllDisplays; _region.Text = options.RegionShortcut; _window.Text = options.WindowShortcut; _screen.Text = options.FullScreenShortcut; }
    private static View Field(string title, View control) { SemanticProperties.SetDescription(control, title); return Theme.Stack(Theme.Label(title, 13), control); }
}

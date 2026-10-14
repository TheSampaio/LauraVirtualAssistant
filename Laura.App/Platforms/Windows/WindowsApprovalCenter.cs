using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;
using Laura.Platform.Windows.Conversation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XStack = Microsoft.UI.Xaml.Controls.StackPanel;
using XWindow = Microsoft.UI.Xaml.Window;

namespace Laura.App.Platforms.Windows;

/// <summary>Independent native approval surface and revocation center; remains accessible from taskbar, tray and HUD.</summary>
public sealed class WindowsApprovalCenter(PermissionManager permissions, ISettingsService settings, IUiLocalizer ui, IServiceProvider services) : IDisposable
{
    private XWindow? _window;
    private XStack? _body;
    private int _tab;
    private readonly HashSet<Guid> _announced = [];
    private readonly CancellationTokenSource _lifetime = new();
    private Action<string, string>? _notify;
    private Func<bool> _mainHidden = () => true;
    private Action? _configure;
    private bool _started;
    private bool _disposing;
    /// <summary>Attaches presentation after the primary native handle exists.</summary>
    public void Start(Action<string, string> notify, Func<bool> mainHidden, Action configure)
    {
        _notify = notify; _mainHidden = mainHidden; _configure = configure;
        if (_started) { return; } _started = true; permissions.Changed += OnChanged;
    }
    /// <summary>Opens the approval center following an explicit HUD or tray action.</summary>
    public void Show() { EnsureWindow(); Render(); WindowsDialogs.Position(_window!, 760, 640); _window!.Activate(); }
    private string T(string key) => ui.Get("ui.permissions." + key);
    private void OnChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(Update);
    private void Update()
    {
        if (_lifetime.IsCancellationRequested) { return; }
        var pending = permissions.PendingRequests;
        var added = pending.Where(item => !_announced.Contains(item.Id)).ToArray();
        _announced.IntersectWith(pending.Select(item => item.Id));
        foreach (var request in added) { _announced.Add(request.Id); }
        if (added.Length > 0)
        {
            EnsureWindow(); _tab = 0; Render(); WindowsDialogs.Position(_window!, 760, 640);
            // Windows focus rules are respected: background requests never activate repeatedly or use topmost.
            _window!.AppWindow.Show(false);
            if (settings.Current.Permissions.Notifications) { _notify?.Invoke(T("required"), T("notification")); }
            if (settings.Current.Permissions.Voice == ApprovalVoiceMode.Always || settings.Current.Permissions.Voice == ApprovalVoiceMode.WhenHidden && _mainHidden())
            { _ = AnnounceAsync(added[0].Id); }
        }
        else if (_window is not null) { Render(); }
    }
    private async Task AnnounceAsync(Guid requestId)
    {
        try
        {
            var live = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GeminiLiveClient>(services);
            if (!live.IsAvailable || settings.Current.Voice.Volume == 0) { return; }
            await live.SpeakNotificationAsync(ui.GetForCulture(settings.Current.Culture, "ui.permissions.announcement"), () => permissions.PendingRequests.Any(item => item.Id == requestId), _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or IOException or System.Net.WebSockets.WebSocketException)
        { /* The independent visual request remains actionable when cloud voice is unavailable. */ }
    }
    private void EnsureWindow()
    {
        if (_window is not null) { return; }
        _window = WindowsDialogs.Create(T("center"));
        _body = new XStack { Spacing = 16, Padding = new Microsoft.UI.Xaml.Thickness(24) };
        _window.Content = new ScrollViewer { Content = _body, Background = WindowsDialogs.Brush(Theme.Background), VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto };
        _window.AppWindow.Closing += (_, args) => { if (_disposing) { return; } args.Cancel = true; _window.AppWindow.Hide(); };
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { _window.AppWindow.Hide(); args.Handled = true; }; _body.KeyboardAccelerators.Add(escape);
    }
    private void Render()
    {
        if (_body is null) { return; }
        _body.Children.Clear();
        var heading = new XStack { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///hud_shield_check_accent.png")), Width = 24, Height = 24 });
        heading.Children.Add(WindowsDialogs.Text("LAURA · " + T("center"), 22)); _body.Children.Add(heading);
        _body.Children.Add(WindowsDialogs.Text(T("help"), 12, true));
        var tabs = new XStack { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        string[] names = [T("pending") + " (" + permissions.PendingRequests.Count + ")", T("history"), T("grants")];
        for (int index = 0; index < names.Length; index++) { int selected = index; tabs.Children.Add(WindowsDialogs.Button(names[index], () => { _tab = selected; Render(); }, index == _tab)); }
        _body.Children.Add(tabs);
        if (permissions.StorageWarning is { } warning) { _body.Children.Add(WindowsDialogs.Text(warning, 12)); }
        if (_tab == 0) { RenderPending(); }
        else if (_tab == 1)
        {
            foreach (var record in permissions.History.Reverse().Take(100))
            { _body.Children.Add(Card(WindowsDialogs.Text($"{record.Time.LocalDateTime:g} · {T("operation." + record.Operation)} · {T("outcome." + record.Outcome)}"), WindowsDialogs.Text(record.Origin + "\n" + record.Scope + "\n" + record.Reason, 12, true))); }
            _body.Children.Add(WindowsDialogs.Button(T("clearHistory"), () => _ = ChangeAsync(permissions.ClearHistory)));
        }
        else
        {
            foreach (var grant in permissions.Grants)
            {
                _body.Children.Add(Card(WindowsDialogs.Text(T("operation." + grant.Operation)), WindowsDialogs.Text(grant.Scope + "\n" + grant.Origin + " · " + T(grant.Persistent ? "persistent" : "session") + "\n" + grant.Created.LocalDateTime.ToString("g", ui.Culture), 12, true),
                    WindowsDialogs.Button(T("revoke"), () => _ = ChangeAsync(() => permissions.Revoke(grant.Id)))));
            }
            _body.Children.Add(WindowsDialogs.Button(T("reset"), () => _ = RestoreAsync()));
        }
        _body.Children.Add(WindowsDialogs.Button(T("settings"), () => _configure?.Invoke()));
    }
    private void RenderPending()
    {
        if (permissions.PendingRequests.Count == 0) { _body!.Children.Add(WindowsDialogs.Text(T("empty"), 14, true)); }
        foreach (var request in permissions.PendingRequests)
        {
            var content = new XStack { Spacing = 12 };
            content.Children.Add(WindowsDialogs.Text(T("operation." + request.Action.Operation), 18));
            content.Children.Add(WindowsDialogs.Text(T("risk." + request.Risk) + " · " + request.Action.Origin + " · " + request.Expires.LocalDateTime.ToString("T", ui.Culture), 12, true));
            if (request.Action.Resources.Length > 0) { content.Children.Add(WindowsDialogs.Text(string.Join("\n", request.Action.Resources))); }
            if (request.Action.Destination.Length > 0) { content.Children.Add(WindowsDialogs.Text(T("destination") + ": " + request.Action.Destination)); }
            if (request.Action.Description.Length > 0)
            { content.Children.Add(new ScrollViewer { Content = WindowsDialogs.Text(request.Action.Description), MaxHeight = 220, VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto }); }
            if (request.Risk == PermissionRisk.Critical) { content.Children.Add(WindowsDialogs.Text(T("criticalHelp"), 12, true)); }
            var choices = new XStack { Spacing = 8 };
            var primaryActions = new XStack { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
            var deny = WindowsDialogs.Button(T("deny"), () => permissions.Decide(request.Id, ApprovalChoice.Deny)); primaryActions.Children.Add(deny);
            var reinforced = new Microsoft.UI.Xaml.Controls.CheckBox { Content = T("reinforced"), Foreground = WindowsDialogs.Brush(Theme.Text) };
            if (request.Risk == PermissionRisk.Critical) { choices.Children.Add(reinforced); }
            var once = WindowsDialogs.Button(T("once"), () => Submit(request.Id, ApprovalChoice.Once), true);
            if (request.Risk == PermissionRisk.Critical)
            { once.IsEnabled = false; reinforced.Checked += (_, _) => once.IsEnabled = true; reinforced.Unchecked += (_, _) => once.IsEnabled = false; }
            primaryActions.Children.Add(once); primaryActions.Children.Add(WindowsDialogs.Button(T("cancel"), () => permissions.Cancel(request.Id)));
            choices.Children.Add(primaryActions);
            if (request.CanRemember)
            {
                bool application = request.Action.Operation == PermissionOperation.ApplicationLaunch;
                var folder = new Microsoft.UI.Xaml.Controls.CheckBox { Content = WindowsDialogs.Text(T("folderScope") + "\n" + Path.GetDirectoryName(request.Action.Resources[0]), 12), Foreground = WindowsDialogs.Brush(Theme.Text) };
                if (!application) { choices.Children.Add(folder); }
                var remembered = new XStack { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
                remembered.Children.Add(WindowsDialogs.Button(T("session"), () => Submit(request.Id, ApprovalChoice.Session, folder.IsChecked == true)));
                remembered.Children.Add(WindowsDialogs.Button(T("persistent"), () => Submit(request.Id, ApprovalChoice.Persistent, folder.IsChecked == true)));
                choices.Children.Add(remembered);
                choices.Children.Add(WindowsDialogs.Text(T(application ? "applicationScopeHelp" : "scopeHelp"), 12, true));
            }
            content.Children.Add(choices); _body!.Children.Add(Card(content));
        }
    }
    private void Submit(Guid id, ApprovalChoice choice, bool folder = false)
    {
        // Remove the approval UI before dispatch, so screen capture and native input target the user's previous application.
        _window?.AppWindow.Hide();
        if (!permissions.Decide(id, choice, folder)) { _window?.AppWindow.Show(false); Render(); }
    }
    internal XWindow NativeWindowForSmoke
    {
        get { RequireSmoke(); return _window ?? throw new InvalidOperationException("No approval window has been created."); }
    }
    internal void InvokeForSmoke(string key)
    {
        RequireSmoke();
        var button = Descendants(_body!).OfType<Microsoft.UI.Xaml.Controls.Button>().Single(item => item.Content as string == T(key));
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
        var provider = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
        provider.Invoke();
    }
    internal void ConfirmCriticalForSmoke()
    {
        RequireSmoke();
        var check = Descendants(_body!).OfType<Microsoft.UI.Xaml.Controls.CheckBox>().Single();
        var toggle = new Microsoft.UI.Xaml.Automation.Peers.CheckBoxAutomationPeer(check);
        ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)toggle.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
        InvokeForSmoke("once");
    }
    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Descendants(Microsoft.UI.Xaml.DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        { foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index))) { yield return child; } }
    }
    private static void RequireSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--permissions-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--screenshot-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--brain-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Approval diagnostics require explicit isolated smoke mode."); }
    }
    private static Microsoft.UI.Xaml.Controls.Border Card(params Microsoft.UI.Xaml.UIElement[] children)
    {
        var body = new XStack { Spacing = 12 }; foreach (var child in children) { body.Children.Add(child); }
        return new() { Child = body, Padding = new Microsoft.UI.Xaml.Thickness(16), CornerRadius = new Microsoft.UI.Xaml.CornerRadius(Theme.PanelRadius),
            Background = WindowsDialogs.Brush(Theme.Surface), BorderBrush = WindowsDialogs.Brush(Theme.Line), BorderThickness = new Microsoft.UI.Xaml.Thickness(1) };
    }
    private async Task ChangeAsync(Action action)
    {
        try { await Task.Run(action).ConfigureAwait(true); Render(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PermissionDeniedException)
        { await WindowsDialogs.AlertAsync("Laura", T("storageError"), "OK").ConfigureAwait(true); }
    }
    private async Task RestoreAsync()
    {
        if (!await WindowsDialogs.ConfirmAsync("Laura", T("resetHelp"), T("reset"), T("cancel")).ConfigureAwait(true)) { return; }
        await ChangeAsync(permissions.Reset).ConfigureAwait(true);
        await settings.UpdateAsync(settings.Current with { Permissions = new() }).ConfigureAwait(true);
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposing) { return; } _disposing = true;
        permissions.Changed -= OnChanged; _lifetime.Cancel();
        if (_window is not null) { _window.Close(); _window = null; } _lifetime.Dispose();
    }
}

using Laura.App.Theming;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.App.Views;

/// <summary>Native permission controls following the existing settings card and save lifecycle.</summary>
public sealed class PermissionSettingsView : ContentView
{
    private readonly Picker _mode;
    private readonly IUiLocalizer _ui;
    private readonly PermissionManager _permissions;
    private readonly Label _modeStatus;
    private readonly Button _disableFullAccess;
    private readonly Picker _voice;
    private readonly Switch _notifications;
    private readonly Switch _applications = new() { OnColor = Theme.Accent };
    private readonly Entry _timeout = Theme.Entry(), _retention = Theme.Entry();
    /// <summary>Creates localized controls without independently saving partial settings.</summary>
    public PermissionSettingsView(ISettingsService settings, IUiLocalizer ui, WindowsApprovalCenter center, PermissionManager permissions)
    {
        string T(string key) => ui.Get("ui.permissions." + key);
        _permissions = permissions; _ui = ui;
        _mode = Theme.Picker([T("ask"), T("automatic"), T("fullAccess")], "");
        _modeStatus = Theme.Label("", 13, Theme.Muted);
        _disableFullAccess = Theme.ActionButton(T("disableFullAccess"), "square", () => { permissions.DeactivateFullAccess(); RefreshModeStatus(); return Task.CompletedTask; });
        _voice = Theme.Picker([T("voiceAlways"), T("voiceHidden"), T("voiceNever")], T("voiceHidden"));
        _notifications = new Switch { OnColor = Theme.Accent };
        _timeout.Keyboard = _retention.Keyboard = Keyboard.Numeric;
        Content = Theme.Stack(Theme.Label(T("settings"), 22), Theme.Label(T("modeHelp"), 13, Theme.Muted),
            Field(T("mode"), _mode), _modeStatus, _disableFullAccess, Field(T("voice"), _voice), Field(T("notifications"), _notifications),
            Field(T("openApplications"), _applications), Theme.Label(T("openApplicationsHelp"), 12, Theme.Muted),
            Field(T("timeout"), _timeout), Field(T("retention"), _retention),
            Theme.Label(T("voiceHelp"), 12, Theme.Muted),
            Theme.ActionButton(T("center"), "shield_check", () => { center.Show(); return Task.CompletedTask; }));
        Reload(settings.Current.Permissions);
        _mode.SelectedIndexChanged += (_, _) => { if (_mode.SelectedIndex != (int)ApprovalMode.FullAccess) { permissions.DeactivateFullAccess(); } RefreshMode(); };
    }
    /// <summary>Gets bounded settings for the existing Save button.</summary>
    public PermissionOptions Options => new PermissionOptions
    {
        Mode = (ApprovalMode)Math.Max(0, _mode.SelectedIndex), Voice = (ApprovalVoiceMode)Math.Max(0, _voice.SelectedIndex),
        Notifications = _notifications.IsToggled, TimeoutSeconds = int.TryParse(_timeout.Text, out int timeout) ? timeout : 120,
        OpenInstalledApplications = _applications.IsToggled,
        HistoryDays = int.TryParse(_retention.Text, out int days) ? days : 30
    }.Sanitized();
    /// <summary>Restores the current persisted configuration when cached settings reopen.</summary>
    public void Reload(PermissionOptions options)
    {
        options = options.Sanitized();
        _mode.SelectedIndex = (int)options.Mode; _voice.SelectedIndex = (int)options.Voice;
        _notifications.IsToggled = options.Notifications;
        _applications.IsToggled = options.OpenInstalledApplications; _applications.IsEnabled = options.Mode == ApprovalMode.Automatic;
        RefreshMode();
        _timeout.Text = options.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _retention.Text = options.HistoryDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>Enables bounded application policy only in automatic mode.</summary>
    private void RefreshMode()
    {
        _applications.IsEnabled = _mode.SelectedIndex == (int)ApprovalMode.Automatic;
        RefreshModeStatus();
    }
    /// <summary>Shows the effective session mode and an immediate native off switch.</summary>
    public void RefreshModeStatus()
    {
        bool selected = _mode.SelectedIndex == (int)ApprovalMode.FullAccess;
        bool active = _permissions.CurrentMode == ApprovalMode.FullAccess;
        _modeStatus.IsVisible = selected;
        _modeStatus.Text = selected ? _ui.Get("ui.permissions." + (active ? "fullAccessActive" : "fullAccessInactive")) : "";
        _disableFullAccess.IsVisible = selected && active;
    }
    /// <summary>Checks the actual native selector exposes all supported modes.</summary>
    internal void VerifyForSmoke()
    {
        if (!Environment.GetCommandLineArgs().Any(argument => argument is "--ui-smoke" or "--permissions-smoke"))
        { throw new InvalidOperationException("Isolated native diagnostics only."); }
        if (_mode.Items.Count != 3 || !Enum.IsDefined(Options.Mode))
        { throw new InvalidOperationException("The native permission selector exposes an unsupported mode."); }
    }
    private static View Field(string title, View control)
    { SemanticProperties.SetDescription(control, title); return Theme.Stack(Theme.Label(title), control); }
}

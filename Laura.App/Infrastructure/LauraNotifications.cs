namespace Laura.App.Infrastructure;

/// <summary>Native notification categories reusable by Laura features.</summary>
public enum LauraNotificationKind
{
    /// <summary>General status.</summary>
    Information,
    /// <summary>Completed operation.</summary>
    Success,
    /// <summary>Attention required.</summary>
    Warning,
    /// <summary>Failed operation.</summary>
    Error,
    /// <summary>Pending local UI approval.</summary>
    Approval
}
/// <summary>Routes generic, nonsensitive notices through the existing Windows tray adapter.</summary>
public sealed class LauraNotifications(Laura.Core.Abstractions.ISettingsService settings)
{
    private Action<string, string, LauraNotificationKind>? _present;
    /// <summary>Binds the adapter after its native handle has been created.</summary>
    public void Bind(Action<string, string, LauraNotificationKind> present) => _present = present;
    /// <summary>Posts a visual notification on the UI thread; OS suppression never decides an approval.</summary>
    public void Publish(string title, string message, LauraNotificationKind kind = LauraNotificationKind.Information) =>
        MainThread.BeginInvokeOnMainThread(() => { if (settings.Current.Permissions.Notifications) { _present?.Invoke(title, message, kind); } });
}

using System.Drawing;
using System.Windows.Forms;
using System.Globalization;
using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>
/// Assistant icon in the system tray and its context menu.
///
/// It is Laura's only permanent visible presence: the window stays hidden,
/// but the icon gives access to settings and exit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ILocalizer _localizer;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _exitItem;

    /// <summary>
    /// Creates the tray icon and builds its menu.
    ///
    /// Args:
    ///     localizer: Source for menu and tooltip text.
    ///     onOpen: Action when choosing "open settings" or double-clicking.
    ///     onExit: Action when choosing "exit".
    /// </summary>
    public TrayIcon(ILocalizer localizer, Action onOpen, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(onOpen);
        ArgumentNullException.ThrowIfNull(onExit);

        _localizer = localizer;

        var menu = new ContextMenuStrip();

        _openItem = new ToolStripMenuItem(_localizer.Get("ui.tray.open"), image: null, (_, _) => onOpen());
        _openItem.Font = new Font(_openItem.Font, FontStyle.Bold);

        _exitItem = new ToolStripMenuItem(_localizer.Get("ui.tray.exit"), image: null, (_, _) => onExit());

        menu.Items.Add(_openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = _localizer.Get("ui.tray.tooltip"),
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => onOpen();
        _localizer.CultureChanged += OnCultureChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _localizer.CultureChanged -= OnCultureChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }

    /// <summary>Refreshes tray labels when the UI language changes on the UI thread.</summary>
    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        _openItem.Text = _localizer.Get("ui.tray.open");
        _exitItem.Text = _localizer.Get("ui.tray.exit");
        _notifyIcon.Text = _localizer.Get("ui.tray.tooltip");
    }

    /// <summary>
    /// Loads the application icon, falling back to the default system icon.
    ///
    /// Returns:
    ///     The tray icon.
    /// </summary>
    private static Icon LoadIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }
}

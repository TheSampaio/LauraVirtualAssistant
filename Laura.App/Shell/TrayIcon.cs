using System.Drawing;
using System.Windows.Forms;
using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>
/// Assistant icon in the system tray and its context menu.
///
/// It is Laura's only permanent visible presence: the window stays hidden,
/// but the icon gives access to settings, listening pause, and exit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ILocalizer _localizer;
    private readonly ToolStripMenuItem _pauseItem;

    /// <summary>
    /// Creates the tray icon and builds its menu.
    ///
    /// Args:
    ///     localizer: Fonte dos textos do menu e da dica.
    ///     onOpen: Action when choosing "open settings" or double-clicking.
    ///     onTogglePause: Action when toggling listening pause.
    ///     onExit: Action when choosing "exit".
    /// </summary>
    public TrayIcon(ILocalizer localizer, Action onOpen, Action onTogglePause, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(onOpen);
        ArgumentNullException.ThrowIfNull(onTogglePause);
        ArgumentNullException.ThrowIfNull(onExit);

        _localizer = localizer;

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem(_localizer.Get("ui.tray.open"), image: null, (_, _) => onOpen());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        _pauseItem = new ToolStripMenuItem(_localizer.Get("ui.tray.mute"), image: null, (_, _) => onTogglePause())
        {
            CheckOnClick = true,
        };

        var exitItem = new ToolStripMenuItem(_localizer.Get("ui.tray.exit"), image: null, (_, _) => onExit());

        menu.Items.Add(openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = _localizer.Get("ui.tray.tooltip"),
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    /// <summary>
    /// Reflects in the menu whether listening is paused.
    ///
    /// Args:
    ///     paused: <see langword="true"/> when voice listening is off.
    /// </summary>
    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    /// <inheritdoc />
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
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
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }
}

using System.Drawing;
using System.Windows.Forms;
using Laura.Core.Abstractions;

namespace Laura.App.Shell;

/// <summary>
/// Ícone da assistente na bandeja do sistema e seu menu de contexto.
///
/// É o único ponto de presença visível permanente de Laura: a janela fica oculta,
/// mas o ícone dá acesso às configurações, à pausa da escuta e ao encerramento.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ILocalizer _localizer;
    private readonly ToolStripMenuItem _pauseItem;

    /// <summary>
    /// Cria o ícone da bandeja e monta seu menu.
    ///
    /// Args:
    ///     localizer: Fonte dos textos do menu e da dica.
    ///     onOpen: Ação ao escolher "abrir configurações" ou dar duplo clique.
    ///     onTogglePause: Ação ao alternar a pausa da escuta.
    ///     onExit: Ação ao escolher "encerrar".
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
    /// Reflete no menu se a escuta está pausada.
    ///
    /// Args:
    ///     paused: <see langword="true"/> quando a escuta por voz está desligada.
    /// </summary>
    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    /// <inheritdoc />
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    /// <summary>
    /// Carrega o ícone da aplicação, recorrendo ao ícone padrão do sistema.
    ///
    /// Returns:
    ///     O ícone da bandeja.
    /// </summary>
    private static Icon LoadIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }
}

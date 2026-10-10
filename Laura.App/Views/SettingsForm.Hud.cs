using System.Diagnostics;
using System.Globalization;
using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Platform.Windows.System;

namespace Laura.App.Views;

public sealed partial class SettingsForm
{
    private Panel _hudHome = null!;
    private Control _hudSettings = null!;
    private AssistantHud _hudCanvas = null!;
    private System.Windows.Forms.Timer? _hudTimer;
    private Label _hudClock = null!;
    private Label _hudDate = null!;
    private Label _hudMemory = null!;
    private Label _hudDisk = null!;
    private Label _hudCpu = null!;
    private Label _hudUptime = null!;
    private Label _hudMicLabel = null!;
    private ProgressBar _hudMemoryBar = null!;
    private FlatButton _hudTalk = null!;
    private readonly Stopwatch _hudUptimeClock = Stopwatch.StartNew();
    private TimeSpan _lastCpuTime;
    private long _lastCpuTimestamp;

    /// <summary>Builds a three-column assistant HUD with settings in its central workspace.</summary>
    private void BuildHudInterface()
    {
        _hudTimer?.Dispose();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Palette.Background, Margin = Padding.Empty, Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.Controls.Add(BuildHudHeader(), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Palette.Background, Margin = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        body.Controls.Add(BuildSystemMonitor(), 0, 0);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background, Margin = Padding.Empty };
        _hudHome = BuildHudHome();
        _hudSettings = BuildHudSettings();
        center.Controls.Add(_hudHome);
        center.Controls.Add(_hudSettings);
        body.Controls.Add(center, 1, 0);

        var activity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Palette.Surface, Margin = Padding.Empty, Padding = new Padding(1, 0, 0, 0),
        };
        activity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        activity.Controls.Add(BuildChatSection(), 0, 0);
        activity.Controls.Add(BuildDailyControls(), 0, 1);
        body.Controls.Add(activity, 2, 0);
        root.Controls.Add(body, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
            BackColor = Palette.Surface, Margin = Padding.Empty, Padding = new Padding(18, 0, 18, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusLabel = HudLabel(string.Empty, 8.5f, Palette.Ready);
        _hintLabel = HudLabel(Text_("ui.hud.hotkey"), 8f);
        _hintLabel.AutoSize = true;
        footer.Controls.Add(_statusLabel, 0, 0);
        footer.Controls.Add(_hintLabel, 1, 0);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        _hudTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _hudTimer.Tick += (_, _) => { if (Visible) { RefreshHudTelemetry(); } };
        _hudTimer.Start();
        RefreshHudTelemetry();
    }

    private Control BuildHudHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Palette.Surface, Padding = new Padding(18, 8, 18, 8), Margin = Padding.Empty };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        var home = HudAction("LAURA", () => SelectSection(Text_("ui.tab.chat")));
        home.IsPrimary = true;
        actions.Controls.Add(home);
        actions.Controls.Add(HudAction(Text_("ui.hud.setup"), () => SelectSection(Text_("ui.tab.models"))));
        actions.Controls.Add(HudAction(Text_("ui.hud.controls"), () => SelectSection(Text_("ui.tab.voice"))));
        header.Controls.Add(actions, 0, 0);
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var name = HudLabel("L A U R A", 21f, Palette.AccentHover);
        name.TextAlign = ContentAlignment.MiddleCenter;
        name.Font = FontFactory.Create(21f, FontStyle.Bold);
        var subtitle = HudLabel(Text_("ui.hud.subtitle"), 8f);
        subtitle.TextAlign = ContentAlignment.MiddleCenter;
        title.Controls.Add(name, 0, 0);
        title.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(title, 1, 0);
        var clock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        clock.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        clock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _hudClock = HudLabel(string.Empty, 21f, Palette.AccentHover);
        _hudDate = HudLabel(string.Empty, 8f);
        _hudClock.TextAlign = _hudDate.TextAlign = ContentAlignment.MiddleRight;
        clock.Controls.Add(_hudClock, 0, 0);
        clock.Controls.Add(_hudDate, 0, 1);
        header.Controls.Add(clock, 2, 0);
        return header;
    }

    private Control BuildSystemMonitor()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10,
            BackColor = Palette.Surface, Padding = new Padding(14, 16, 14, 14), Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.Controls.Add(HudLabel(Text_("ui.hud.monitor"), 9f, Palette.AccentHover), 0, 0);
        _hudCpu = HudLabel(string.Empty, 9f);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.cpu"), _hudCpu), 0, 1);
        _hudMemory = HudLabel(string.Empty, 9f);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.ram"), _hudMemory), 0, 2);
        _hudMemoryBar = new ProgressBar { Dock = DockStyle.Top, Height = 5, Margin = new Padding(0, 0, 0, 16) };
        panel.Controls.Add(_hudMemoryBar, 0, 3);
        _hudDisk = HudLabel(string.Empty, 9f);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.disk"), _hudDisk), 0, 4);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.backend"), HudLabel("CPU", 11f, Palette.AccentHover)), 0, 5);
        _hudUptime = HudLabel(string.Empty, 9f);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.uptime"), _hudUptime), 0, 6);
        for (int index = 1; index <= 6; index++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        panel.Controls.Add(MonitorCard(Text_("ui.hud.system"), HudLabel("Windows · x64", 9f)), 0, 8);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(MonitorCard(Text_("ui.hud.privacy"), HudLabel(Text_("ui.hud.local"), 9f, Palette.Ready)), 0, 9);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return panel;
    }

    private static Control MonitorCard(string caption, Label value)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2,
            BackColor = Palette.Field, Padding = new Padding(9), Margin = new Padding(0, 0, 0, 12) };
        card.Controls.Add(HudLabel(caption, 7.5f, Palette.AccentHover), 0, 0);
        value.AutoSize = true;
        value.Dock = DockStyle.Top;
        value.Padding = new Padding(0, 7, 0, 0);
        card.Controls.Add(value, 0, 1);
        return card;
    }

    private Panel BuildHudHome()
    {
        var home = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Palette.Background, Padding = new Padding(20, 10, 20, 12) };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        content.Controls.Add(HudLabel(Text_("ui.hud.core"), 9f, Palette.AccentHover), 0, 0);
        _hudCanvas = new AssistantHud { Dock = DockStyle.Fill, State = _engine.State,
            StateText = Text_("ui.status.idle"), Caption = Text_("ui.hud.awaiting") };
        content.Controls.Add(_hudCanvas, 0, 1);
        var prompt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Palette.Surface, Padding = new Padding(16) };
        prompt.Controls.Add(HudLabel(Text_("ui.hud.ready"), 10f, Palette.TextPrimary), 0, 0);
        prompt.Controls.Add(HudLabel(Text_("ui.hud.prompt"), 9f), 0, 1);
        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        quick.Controls.Add(HudAction(Text_("ui.tab.models"), () => SelectSection(Text_("ui.tab.models"))));
        quick.Controls.Add(HudAction(Text_("ui.tab.memory"), () => SelectSection(Text_("ui.tab.memory"))));
        quick.Controls.Add(HudAction(Text_("ui.chat.aiOptions"), () => SelectSection(Text_("ui.hud.ai"))));
        prompt.Controls.Add(quick, 0, 2);
        content.Controls.Add(prompt, 0, 2);
        home.Controls.Add(content);
        return home;
    }

    private Control BuildHudSettings()
    {
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(12), BackColor = Palette.Background };
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        foreach (string key in new[] { "ui.tab.models", "ui.tab.memory", "ui.hud.ai", "ui.tab.general", "ui.tab.user", "ui.tab.voice" })
        {
            string caption = Text_(key);
            var button = new NavButton(caption, string.Empty) { Width = 130, Height = 36, Margin = new Padding(2), Font = FontFactory.Create(9f) };
            button.Click += (_, _) => SelectSection(caption);
            _navButtons.Add(button);
            tabs.Controls.Add(button);
        }
        settings.Controls.Add(tabs, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill };
        pages.Controls.Add(BuildGeneralSection());
        pages.Controls.Add(BuildUserSection());
        pages.Controls.Add(BuildVoiceSection());
        var ai = new Panel { Dock = DockStyle.Fill, Visible = false };
        _chatAiSettingsPanel = BuildChatAiSettings();
        ai.Controls.Add(_chatAiSettingsPanel);
        _sections[Text_("ui.hud.ai")] = ai;
        pages.Controls.Add(ai);
        pages.Controls.Add(BuildModelsSection());
        pages.Controls.Add(BuildMemorySection());
        settings.Controls.Add(pages, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 8, 0, 0) };
        var save = HudAction(Text_("ui.actions.save"), async () => await SaveAsync().ConfigureAwait(true));
        save.IsPrimary = true;
        actions.Controls.Add(save);
        actions.Controls.Add(HudAction(Text_("ui.actions.restoreDefaults"), RestoreDefaults));
        actions.Controls.Add(HudAction(Text_("ui.hud.back"), () => SelectSection(Text_("ui.tab.chat"))));
        _footerPanel = actions;
        settings.Controls.Add(actions, 0, 2);
        return settings;
    }

    private Control BuildActivityHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Palette.Surface, Padding = new Padding(16, 12, 16, 6) };
        header.Controls.Add(HudLabel(Text_("ui.hud.activity"), 9f, Palette.AccentHover), 0, 0);
        _chatStatusLabel = HudLabel(Text_("ui.status.idle"), 8f);
        header.Controls.Add(_chatStatusLabel, 0, 1);
        _chatModelLabel = HudLabel(string.Empty, 8f);
        _chatModelLabel.AutoEllipsis = true;
        header.Controls.Add(_chatModelLabel, 0, 2);
        return header;
    }

    private Control BuildDailyControls()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Palette.Surface, Padding = new Padding(16, 0, 16, 12) };
        for (int index = 0; index < 3; index++) { panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3)); }
        var interrupt = HudAction(Text_("ui.hud.interrupt"), () =>
        {
            _inference?.Cancel(); _engine.StopSpeaking(); _recordingStop?.Cancel();
        });
        interrupt.Dock = DockStyle.Fill;
        interrupt.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(interrupt, 0, 0);
        _hudTalk = HudAction(Text_("ui.voice.talk"), async () => await ToggleVoiceInputAsync().ConfigureAwait(true));
        _hudTalk.Dock = DockStyle.Fill;
        _hudTalk.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(_hudTalk, 0, 1);
        var mic = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _hudMicLabel = HudLabel(Text_("ui.hud.micMuted"), 8f);
        _hudMicLabel.AutoSize = true;
        _hudMicLabel.Margin = new Padding(0, 12, 8, 0);
        mic.Controls.Add(_hudMicLabel);
        mic.Controls.Add(HudAction(Text_("ui.hud.microphone"), () =>
        {
            _muteMic.Checked = !_muteMic.Checked;
            RefreshHudMicrophone();
        }));
        panel.Controls.Add(mic, 0, 2);
        _muteMic.CheckedChanged += (_, _) => RefreshHudMicrophone();
        return panel;
    }

    private void RefreshHudMicrophone()
    {
        if (_hudMicLabel is null) { return; }
        _hudMicLabel.Text = Text_(_muteMic.Checked ? "ui.hud.micMuted" : "ui.hud.micReady");
        _hudMicLabel.ForeColor = _muteMic.Checked ? Palette.TextSecondary : Palette.Ready;
        _hudTalk.ForeColor = _muteMic.Checked ? Palette.TextSecondary : Palette.TextPrimary;
        _hudTalk.Text = Text_(_recordingStop is null ? "ui.voice.talk" : "ui.voice.finish");
        _hudCanvas.Recording = _recordingStop is not null;
    }

    private void RefreshHudTelemetry()
    {
        _hudClock.Text = DateTime.Now.ToString("HH:mm:ss", _localizer.Culture);
        _hudDate.Text = DateTime.Now.ToString("ddd, dd MMM yyyy", _localizer.Culture);
        _hudUptime.Text = _hudUptimeClock.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        _hudCanvas.StateText = _recordingStop is not null ? Text_("ui.hud.recording") : _localizer.Get(_engine.State switch
        {
            Laura.Core.Engine.AssistantState.Ready => "ui.status.idle",
            Laura.Core.Engine.AssistantState.Working => "ui.status.thinking",
            Laura.Core.Engine.AssistantState.Speaking => "ui.status.speaking",
            _ => "ui.status.stopped",
        });
        RefreshHudMicrophone();
        try
        {
            var hardware = WindowsHardwareProbe.Detect(AppContext.BaseDirectory);
            double used = hardware.TotalRam > 0 ? 100d * (hardware.TotalRam - hardware.AvailableRam) / hardware.TotalRam : 0;
            _hudMemory.Text = used.ToString("F0", _localizer.Culture) + "% · " + (hardware.TotalRam / 1073741824d).ToString("F1", _localizer.Culture) + " GB";
            _hudMemoryBar.Value = Math.Clamp((int)used, 0, 100);
            _hudDisk.Text = (hardware.FreeDisk / 1073741824d).ToString("F0", _localizer.Culture) + " GB";
            using var process = Process.GetCurrentProcess();
            TimeSpan cpu = process.TotalProcessorTime;
            long timestamp = Stopwatch.GetTimestamp();
            double usage = _lastCpuTimestamp == 0 ? 0 : (cpu - _lastCpuTime).TotalSeconds / Stopwatch.GetElapsedTime(_lastCpuTimestamp, timestamp).TotalSeconds / Environment.ProcessorCount * 100;
            _hudCpu.Text = usage.ToString("F1", _localizer.Culture) + "% · " + Environment.ProcessorCount.ToString(_localizer.Culture);
            _lastCpuTime = cpu;
            _lastCpuTimestamp = timestamp;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _hudDisk.Text = _hudMemory.Text = _hudCpu.Text = "—";
        }
    }

    private static Label HudLabel(string text, float size, Color? color = null) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false, UseMnemonic = false,
        ForeColor = color ?? Palette.TextSecondary, Font = new Font("Consolas", size),
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
    };

    private static FlatButton HudAction(string text, Action action)
    {
        var button = new FlatButton { Text = text, IsPrimary = false, AutoSize = true,
            Font = FontFactory.Create(8.5f), Height = 34, MinimumSize = new Size(0, 32),
            Padding = new Padding(12, 6, 12, 6), Margin = new Padding(2, 8, 4, 0) };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Palette.Border;
        button.Click += (_, _) => action();
        return button;
    }
}

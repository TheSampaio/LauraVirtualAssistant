using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Laura.App.Controls;
using Laura.App.Infrastructure;
using Laura.App.Interop;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Localization;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Laura.App.Views;

/// <summary>
/// Laura's settings window: general behaviour, the user, voice, listening and the
/// generative mode.
///
/// It stays hidden and is brought to the front by <c>Alt + L</c>; closing it from the
/// title bar only hides it, keeping the assistant active in the tray.
/// </summary>
public sealed class SettingsForm : Form
{
    private const int NavigationWidth = 210;
    private const int MultilineHeight = 96;

    private readonly ISettingsService _settingsService;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ILocalizer _localizer;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IAssistantEngine _engine;
    private readonly IUiDispatcher _dispatcher;
    private readonly IModelCatalog _modelCatalog;
    private readonly IAudioDeviceCatalog _audioDevices;
    private readonly IUserContext _userContext;
    private readonly ILogger<SettingsForm> _logger;

    private readonly Dictionary<string, Panel> _sections = new(StringComparer.Ordinal);
    private readonly List<NavButton> _navButtons = [];
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 120 };

    private SettingsEditModel _model;
    private string _builtCulture = string.Empty;
    private Label _statusLabel = null!;
    private Label _hintLabel = null!;
    private Panel _chatViewport = null!;
    private FlowLayoutPanel _chatList = null!;
    private Label _chatEmptyLabel = null!;
    private TextBox _chatInputBox = null!;
    private Panel _chatScrollTrack = null!;
    private Panel _chatScrollThumb = null!;
    private CancellationTokenSource? _previewCts;
    private bool _closingToTray = true;

    // Controls whose values are read on save.
    private TextBox _nicknameBox = null!;
    private ToggleSwitch _greetToggle = null!;
    private ToggleSwitch _hourlyToggle = null!;
    private ToggleSwitch _startupToggle = null!;
    private ComboBox _voiceCombo = null!;
    private Slider _rateSlider = null!;
    private Slider _pitchSlider = null!;
    private Slider _volumeSlider = null!;
    private ToggleSwitch _recognitionToggle = null!;
    private ComboBox _microphoneCombo = null!;
    private ToggleSwitch _noiseToggle = null!;
    private TextBox _wakePhrasesBox = null!;
    private Slider _confidenceSlider = null!;
    private Slider _timeoutSlider = null!;
    private ToggleSwitch _inlineToggle = null!;
    private ToggleSwitch _aiToggle = null!;
    private TextBox _endpointBox = null!;
    private ComboBox _modelCombo = null!;
    private TextBox _personaBox = null!;

    /// <summary>
    /// Initializes the window and builds the whole interface.
    ///
    /// Args:
    ///     settingsService: Settings service read and updated by the window.
    ///     synthesizer: Synthesis engine used for the preview and the voice list.
    ///     localizer: Source of the interface texts.
    ///     startupRegistration: Auto-start registration applied on save.
    ///     engine: The assistant engine, observed to show the listening state.
    ///     dispatcher: Marshals engine events onto the UI thread.
    ///     modelCatalog: Catalog of installed generative models.
    ///     audioDevices: Catalog of capture devices.
    ///     userContext: Provides the account name used as the nickname default.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public SettingsForm(
        ISettingsService settingsService,
        ISpeechSynthesizer synthesizer,
        ILocalizer localizer,
        IStartupRegistration startupRegistration,
        IAssistantEngine engine,
        IUiDispatcher dispatcher,
        IModelCatalog modelCatalog,
        IAudioDeviceCatalog audioDevices,
        IUserContext userContext,
        ILogger<SettingsForm> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(startupRegistration);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(modelCatalog);
        ArgumentNullException.ThrowIfNull(audioDevices);
        ArgumentNullException.ThrowIfNull(userContext);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsService = settingsService;
        _synthesizer = synthesizer;
        _localizer = localizer;
        _startupRegistration = startupRegistration;
        _engine = engine;
        _dispatcher = dispatcher;
        _modelCatalog = modelCatalog;
        _audioDevices = audioDevices;
        _userContext = userContext;
        _logger = logger;

        _model = CreateModel(settingsService.Current);

        InitializeShell();
        BuildInterface();

        _engine.StateChanged += OnEngineStateChanged;
        _engine.ConversationMessageReceived += OnConversationMessageReceived;
    }

    /// <summary>
    /// Brings the hidden window back to the front, reloading the settings.
    ///
    /// If the language changed since the last build, the interface is rebuilt — the
    /// labels are resolved at construction and do not translate themselves.
    /// </summary>
    public void ShowFromTray()
    {
        _model = CreateModel(_settingsService.Current);

        if (!string.Equals(_builtCulture, _localizer.Culture.Name, StringComparison.Ordinal))
        {
            RebuildInterface();
        }
        else
        {
            LoadModelIntoControls();
        }

        UpdateEngineStatus(_engine.State);

        Show();

        if (WindowState is FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    /// <summary>
    /// Actually closes the window, ending the application.
    ///
    /// Used when quitting from the tray; bypasses the hide-only behaviour.
    /// </summary>
    public void CloseToExit()
    {
        _closingToTray = false;
        Close();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.UseDarkTitleBar(Handle);
    }

    /// <inheritdoc />
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing from the title bar hides the window; Laura stays in the tray.
        if (_closingToTray && e.CloseReason is CloseReason.UserClosing)
        {
            e.Cancel = true;
            CancelPreview();
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <inheritdoc />
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!IsHandleCreated || !_closingToTray)
        {
            return;
        }

        _ = _engine.SetForegroundListeningAsync(Visible);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.StateChanged -= OnEngineStateChanged;
            _engine.ConversationMessageReceived -= OnConversationMessageReceived;
            _toolTip.Dispose();
            _previewCts?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Creates an edit model from the given settings, wiring the catalogs it needs.
    ///
    /// Args:
    ///     settings: Settings to copy into the model.
    ///
    /// Returns:
    ///     A fresh edit model.
    /// </summary>
    private SettingsEditModel CreateModel(LauraSettings settings) =>
        new(settings, _synthesizer, _localizer, _modelCatalog, _audioDevices);

    // === Window structure ===

    /// <summary>
    /// Configures the window's general properties.
    /// </summary>
    private void InitializeShell()
    {
        Text = _localizer.Get("ui.window.title");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Palette.Background;
        ForeColor = Palette.TextPrimary;
        Font = FontFactory.Create(9.5f);
        ClientSize = new Size(920, 660);
        TryApplyWindowIcon();
    }

    /// <summary>
    /// Rebuilds the interface from scratch, applying the active language.
    /// </summary>
    private void RebuildInterface()
    {
        SuspendLayout();

        Controls.Clear();
        _sections.Clear();
        _navButtons.Clear();

        Text = _localizer.Get("ui.window.title");
        BuildInterface();

        ResumeLayout(performLayout: true);
    }

    /// <summary>
    /// Builds the side navigation, the body and the footer.
    /// </summary>
    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Palette.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavigationWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildNavigation(), 0, 0);
        root.Controls.Add(BuildContentArea(), 1, 0);

        Controls.Add(root);

        _builtCulture = _localizer.Culture.Name;
        LoadModelIntoControls();
        SelectSection(_localizer.Get("ui.tab.chat"));
    }

    /// <summary>
    /// Builds the side navigation column.
    ///
    /// Returns:
    ///     The navigation panel.
    /// </summary>
    private Control BuildNavigation()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            Padding = new Padding(Palette.Unit * 2),
        };

        var navStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Palette.Background,
        };

        navStack.Controls.Add(new Label
        {
            Text = "Laura",
            AutoSize = true,
            ForeColor = Palette.TextPrimary,
            Font = FontFactory.Create(18f, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0),
        });

        navStack.Controls.Add(new Label
        {
            Text = _localizer.Get("ui.window.subtitle"),
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(NavigationWidth - Palette.Unit * 5, 0),
            ForeColor = Palette.TextSecondary,
            Font = FontFactory.Create(8.5f),
            Margin = new Padding(4, 2, 0, Palette.Unit * 2),
        });

        // Conversation comes first because it is the primary interaction surface.
        AddNavButton(navStack, _localizer.Get("ui.tab.chat"), "💬");
        AddNavButton(navStack, _localizer.Get("ui.tab.general"), "⚙");
        AddNavButton(navStack, _localizer.Get("ui.tab.user"), "\U0001F464");
        AddNavButton(navStack, _localizer.Get("ui.tab.voice"), "\U0001F5E3");
        AddNavButton(navStack, _localizer.Get("ui.tab.recognition"), "\U0001F3A4");
        AddNavButton(navStack, _localizer.Get("ui.tab.ai"), "✨");

        panel.Controls.Add(navStack);
        return panel;
    }

    /// <summary>
    /// Creates a navigation item and links it to its section.
    ///
    /// Args:
    ///     container: Stack the item is added to.
    ///     caption: Section label, also used as the key.
    ///     glyph: Item symbol.
    /// </summary>
    private void AddNavButton(Control container, string caption, string glyph)
    {
        var button = new NavButton(caption, glyph)
        {
            Width = NavigationWidth - Palette.Unit * 5,
            Margin = new Padding(0, 2, 0, 2),
        };

        button.Click += (_, _) => SelectSection(caption);

        _navButtons.Add(button);
        container.Controls.Add(button);
    }

    /// <summary>
    /// Builds the content area with the scrolling body and the action footer.
    ///
    /// Returns:
    ///     The content-area panel.
    /// </summary>
    private Control BuildContentArea()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.Background,
            Padding = new Padding(0, Palette.Unit * 2, Palette.Unit * 3, Palette.Unit * 2),
        };
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background };
        host.Controls.Add(BuildGeneralSection());
        host.Controls.Add(BuildChatSection());
        host.Controls.Add(BuildUserSection());
        host.Controls.Add(BuildVoiceSection());
        host.Controls.Add(BuildRecognitionSection());
        host.Controls.Add(BuildAiSection());

        container.Controls.Add(host, 0, 0);
        container.Controls.Add(BuildFooter(), 0, 1);

        return container;
    }

    /// <summary>
    /// Builds the footer with the assistant state and the action buttons.
    ///
    /// Returns:
    ///     The footer panel.
    /// </summary>
    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Background,
            Margin = new Padding(0, Palette.Unit, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statusStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Background,
            Anchor = AnchorStyles.Left,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            ForeColor = Palette.TextSecondary,
            Font = FontFactory.Create(9f, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 2),
        };

        _hintLabel = new Label
        {
            Text = _localizer.Get("ui.footer.hotkey"),
            AutoSize = true,
            UseMnemonic = false,
            ForeColor = Palette.TextSecondary,
            Font = FontFactory.Create(8.5f),
            Margin = new Padding(4, 0, 0, 0),
        };

        statusStack.Controls.Add(_statusLabel);
        statusStack.Controls.Add(_hintLabel);

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            WrapContents = false,
            BackColor = Palette.Background,
        };

        var restoreButton = new FlatButton { Text = _localizer.Get("ui.actions.restoreDefaults"), IsPrimary = false };
        restoreButton.Click += (_, _) => RestoreDefaults();

        var stopButton = new FlatButton { Text = "■", IsPrimary = false };
        _toolTip.SetToolTip(stopButton, _localizer.Get("ui.chat.stop"));
        stopButton.Click += (_, _) => _engine.StopSpeaking();

        var saveButton = new FlatButton { Text = _localizer.Get("ui.actions.save"), IsPrimary = true };
        saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);

        actions.Controls.Add(stopButton);
        actions.Controls.Add(restoreButton);
        actions.Controls.Add(saveButton);

        footer.Controls.Add(statusStack, 0, 0);
        footer.Controls.Add(actions, 1, 0);

        return footer;
    }

    // === Sections ===

    /// <summary>
    /// Builds the general section.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildGeneralSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(_localizer.Get("ui.tab.general"));

        AddRow(stack, SettingRowFactory.Heading(Text_("ui.general.section")));

        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.general.greetOnStartup"), string.Empty, out _greetToggle));
        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.general.announceHourly"), string.Empty, out _hourlyToggle));
        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.general.startWithWindows"), string.Empty, out _startupToggle));

        return page;
    }

    /// <summary>
    /// Builds the chat section.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildChatSection()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            Visible = false,
            Padding = new Padding(Palette.Unit * 2, 0, Palette.Unit, 0),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Background,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(SettingRowFactory.Heading(Text_("ui.chat.section")), 0, 0);

        var chatHost = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            CornerRadius = 12,
            Padding = new Padding(Palette.Unit * 2),
            Margin = new Padding(0, 0, 0, Palette.Unit),
        };

        var chatGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Palette.Surface,
        };
        chatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));

        _chatViewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
        };
        _chatViewport.MouseWheel += (_, args) => ScrollChatBy(-args.Delta / 3);
        _chatViewport.Resize += (_, _) => LayoutChatMessages();

        _chatList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Palette.Surface,
            Location = Point.Empty,
            Width = 1,
        };
        _chatList.MouseWheel += (_, args) => ScrollChatBy(-args.Delta / 3);
        _chatList.ControlAdded += (_, _) => UpdateChatScroll();
        _chatList.ControlRemoved += (_, _) => UpdateChatScroll();

        _chatEmptyLabel = new Label
        {
            Text = Text_("ui.chat.empty"),
            AutoSize = true,
            UseMnemonic = false,
            ForeColor = Palette.TextSecondary,
            Font = FontFactory.Create(9f),
            Margin = new Padding(0),
        };

        _chatList.Controls.Add(_chatEmptyLabel);
        _chatViewport.Controls.Add(_chatList);

        _chatScrollTrack = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.ScrollTrack,
            CornerRadius = 4,
            Margin = new Padding(3, 0, 0, 0),
        };

        _chatScrollThumb = new RoundedPanel
        {
            Width = 6,
            Height = 40,
            BackColor = Palette.ScrollThumb,
            CornerRadius = 4,
            Cursor = Cursors.Hand,
            Left = 2,
        };
        _chatScrollTrack.Controls.Add(_chatScrollThumb);
        _chatScrollTrack.Resize += (_, _) => UpdateChatScroll();
        _chatScrollTrack.MouseDown += (_, args) => JumpChatScroll(args.Y);
        _chatScrollThumb.MouseDown += (_, args) => BeginChatThumbDrag(args.Y);

        chatGrid.Controls.Add(_chatViewport, 0, 0);
        chatGrid.Controls.Add(_chatScrollTrack, 1, 0);
        chatHost.Controls.Add(chatGrid);
        layout.Controls.Add(chatHost, 0, 1);

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Background,
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Control inputPanel = InputFactory.CreateTextBox(0, out _chatInputBox);
        inputPanel.Dock = DockStyle.Fill;
        _chatInputBox.PlaceholderText = Text_("ui.chat.input");
        _chatInputBox.KeyDown += async (_, args) =>
        {
            if (args.KeyCode is Keys.Enter && !args.Shift)
            {
                args.SuppressKeyPress = true;
                await SendChatInputAsync().ConfigureAwait(true);
            }
        };

        var sendButton = new FlatButton { Text = Text_("ui.chat.send"), IsPrimary = true };
        sendButton.Click += async (_, _) => await SendChatInputAsync().ConfigureAwait(true);

        inputRow.Controls.Add(inputPanel, 0, 0);
        inputRow.Controls.Add(sendButton, 1, 0);

        layout.Controls.Add(inputRow, 0, 2);
        page.Controls.Add(layout);
        _sections[_localizer.Get("ui.tab.chat")] = page;
        ReplayChatHistory();

        return page;
    }

    /// <summary>
    /// Builds the user section, where the nickname is set.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildUserSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(_localizer.Get("ui.tab.user"));

        AddRow(stack, SettingRowFactory.Heading(Text_("ui.user.section")));

        string nicknameHint = _localizer.Get("ui.user.nicknameHint", _userContext.AccountName);
        AddRow(stack, SettingRowFactory.TextRow(Text_("ui.user.nickname"), nicknameHint, out _nicknameBox));
        _toolTip.SetToolTip(_nicknameBox, nicknameHint);

        return page;
    }

    /// <summary>
    /// Builds the voice section.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildVoiceSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(_localizer.Get("ui.tab.voice"));

        AddRow(stack, SettingRowFactory.Heading(Text_("ui.voice.section")));

        AddRow(stack, SettingRowFactory.ComboRow(
            Text_("ui.voice.voice"), Text_("ui.voice.voiceHint"), out _voiceCombo));
        Tip(_voiceCombo, "ui.voice.voiceHint");

        AddRow(stack, SettingRowFactory.SliderRow(
            Text_("ui.voice.rate"), VoiceProfile.MinimumProsody, VoiceProfile.MaximumProsody, FormatSigned, out _rateSlider));
        AddRow(stack, SettingRowFactory.SliderRow(
            Text_("ui.voice.pitch"), VoiceProfile.MinimumProsody, VoiceProfile.MaximumProsody, FormatSigned, out _pitchSlider));
        AddRow(stack, SettingRowFactory.SliderRow(
            Text_("ui.voice.volume"), 0, 100, FormatPercent, out _volumeSlider));

        var previewButton = new FlatButton
        {
            Text = Text_("ui.voice.preview"),
            IsPrimary = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, Palette.Unit, 0, 0),
        };
        previewButton.Click += async (_, _) => await PreviewVoiceAsync().ConfigureAwait(true);

        AddRow(stack, previewButton);
        return page;
    }

    /// <summary>
    /// Builds the listening section.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildRecognitionSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(_localizer.Get("ui.tab.recognition"));

        AddRow(stack, SettingRowFactory.Heading(Text_("ui.recognition.section")));

        AddRow(stack, SettingRowFactory.Toggle(
            Text_("ui.recognition.enabled"), Text_("ui.recognition.enabledHint"), out _recognitionToggle));
        Tip(_recognitionToggle, "ui.recognition.enabledHint");

        AddRow(stack, SettingRowFactory.ComboRow(
            Text_("ui.recognition.microphone"), Text_("ui.recognition.microphoneHint"), out _microphoneCombo));
        Tip(_microphoneCombo, "ui.recognition.microphoneHint");

        AddRow(stack, SettingRowFactory.Toggle(
            Text_("ui.recognition.noise"), Text_("ui.recognition.noiseHint"), out _noiseToggle));
        Tip(_noiseToggle, "ui.recognition.noiseHint");

        AddRow(stack, SettingRowFactory.MultilineRow(
            Text_("ui.recognition.wakePhrases"), Text_("ui.recognition.wakePhrasesHint"), MultilineHeight, out _wakePhrasesBox));
        Tip(_wakePhrasesBox, "ui.recognition.wakePhrasesHint");

        AddRow(stack, SettingRowFactory.SliderRow(
            Text_("ui.recognition.confidence"), 0, 100, FormatPercent, out _confidenceSlider));
        Tip(_confidenceSlider, "ui.recognition.confidenceHint");

        AddRow(stack, SettingRowFactory.SliderRow(
            Text_("ui.recognition.timeout"), 3, 30, FormatSeconds, out _timeoutSlider));
        Tip(_timeoutSlider, "ui.recognition.timeout");

        AddRow(stack, SettingRowFactory.Toggle(
            Text_("ui.recognition.inlineCommand"), Text_("ui.recognition.inlineCommandHint"), out _inlineToggle));
        Tip(_inlineToggle, "ui.recognition.inlineCommandHint");

        return page;
    }

    /// <summary>
    /// Builds the generative-mode section.
    ///
    /// Returns:
    ///     The section panel.
    /// </summary>
    private Panel BuildAiSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(_localizer.Get("ui.tab.ai"));

        AddRow(stack, SettingRowFactory.Heading(Text_("ui.ai.section")));

        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.ai.enabled"), Text_("ui.ai.hint"), out _aiToggle));
        Tip(_aiToggle, "ui.ai.hint");

        AddRow(stack, new Label
        {
            Text = Text_("ui.ai.notConfigured"),
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(540, 0),
            ForeColor = Palette.TextSecondary,
            Font = FontFactory.Create(8.5f),
            Margin = new Padding(4, 0, 0, Palette.Unit),
            Anchor = AnchorStyles.Left,
        });

        AddRow(stack, SettingRowFactory.TextRow(Text_("ui.ai.endpoint"), Text_("ui.ai.endpointHint"), out _endpointBox));
        Tip(_endpointBox, "ui.ai.endpointHint");

        AddRow(stack, SettingRowFactory.ComboRow(Text_("ui.ai.model"), Text_("ui.ai.modelHint"), out _modelCombo));
        Tip(_modelCombo, "ui.ai.modelHint");

        AddRow(stack, SettingRowFactory.MultilineRow(
            Text_("ui.ai.persona"), Text_("ui.ai.personaHint"), MultilineHeight, out _personaBox));
        Tip(_personaBox, "ui.ai.personaHint");

        return page;
    }

    /// <summary>
    /// Creates the scrolling page of a section and registers it in the navigation.
    ///
    /// Args:
    ///     key: Section title, used as the navigation key.
    ///
    /// Returns:
    ///     The page and the grid the rows stack into.
    /// </summary>
    private (Panel Page, TableLayoutPanel Stack) CreateSection(string key)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            AutoScroll = true,
            Visible = false,
            Padding = new Padding(Palette.Unit * 2, 0, Palette.Unit, 0),
        };

        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Background,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        page.Controls.Add(stack);
        _sections[key] = page;

        return (page, stack);
    }

    /// <summary>
    /// Appends a row to a section's stack.
    ///
    /// Args:
    ///     stack: Section grid.
    ///     row: Row control.
    /// </summary>
    private static void AddRow(TableLayoutPanel stack, Control row)
    {
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(row, 0, stack.RowCount - 1);
    }

    /// <summary>
    /// Attaches a tooltip, resolved from the active language, to a control.
    ///
    /// Args:
    ///     control: Control the tooltip is shown for.
    ///     key: Language key of the tooltip text.
    /// </summary>
    private void Tip(Control control, string key) => _toolTip.SetToolTip(control, _localizer.Get(key));

    // === Interaction ===

    /// <summary>
    /// Shows the requested section and highlights the matching navigation item.
    ///
    /// Args:
    ///     key: Title of the section to show.
    /// </summary>
    private void SelectSection(string key)
    {
        foreach ((string sectionKey, Panel page) in _sections)
        {
            page.Visible = string.Equals(sectionKey, key, StringComparison.Ordinal);
        }

        foreach (NavButton button in _navButtons)
        {
            button.Selected = string.Equals(button.Caption, key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Plays a sample phrase with the timbre being edited.
    ///
    /// The preview uses the synthesizer directly, not the engine, so it reflects the
    /// not-yet-saved adjustments.
    ///
    /// Returns:
    ///     A task that completes when the utterance ends or is replaced.
    /// </summary>
    private async Task PreviewVoiceAsync()
    {
        CommitControlsToModel();
        CancelPreview();

        var cts = new CancellationTokenSource();
        _previewCts = cts;

        var request = new SpeechRequest(Text_("ui.voice.previewText"), _model.BuildVoiceProfile(), _model.Culture);

        try
        {
            await _synthesizer.SpeakAsync(request, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer preview replaced this one.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Voice preview failed.");
        }
    }

    /// <summary>
    /// Cancels the preview in progress, if any.
    /// </summary>
    private void CancelPreview()
    {
        if (_previewCts is null)
        {
            return;
        }

        _synthesizer.CancelSpeech();
        _previewCts.Cancel();
        _previewCts.Dispose();
        _previewCts = null;
    }

    /// <summary>
    /// Reverts every control to the factory defaults.
    /// </summary>
    private void RestoreDefaults()
    {
        _model = CreateModel(LauraSettings.Default);
        LoadModelIntoControls();
    }

    /// <summary>
    /// Saves the settings and applies the system-level side effects.
    ///
    /// Returns:
    ///     A task that completes when the settings are persisted.
    /// </summary>
    private async Task SaveAsync()
    {
        CommitControlsToModel();
        CancelPreview();

        try
        {
            LauraSettings applied = await _settingsService.UpdateAsync(_model.ToSettings()).ConfigureAwait(true);
            _startupRegistration.SetEnabled(applied.StartWithWindows);

            _model = CreateModel(applied);
            ShowTransientStatus(_localizer.Get("ui.status.saved"));

            // Laura confirms the save out loud, with rotating wording so it never
            // feels canned. Fire-and-forget so the UI stays responsive.
            _ = _engine.SpeakAsync(_localizer.Get(LocalizationKeys.Assistant.SettingsSaved));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save the settings.");
            ShowTransientStatus(_localizer.Get("ui.status.saveFailed"));
        }
    }

    // === Engine state ===

    /// <summary>
    /// Marshals the engine state change onto the UI thread.
    ///
    /// Args:
    ///     sender: The assistant engine.
    ///     state: New state.
    /// </summary>
    private void OnEngineStateChanged(object? sender, AssistantState state) =>
        _dispatcher.Post(() => UpdateEngineStatus(state));

    /// <summary>
    /// Marshals a conversation message onto the UI thread.
    ///
    /// Args:
    ///     sender: The assistant engine.
    ///     message: Message to display.
    /// </summary>
    private void OnConversationMessageReceived(object? sender, ConversationMessage message) =>
        _dispatcher.Post(() => AppendChatMessage(message));

    /// <summary>
    /// Sends the typed chat input to the assistant.
    ///
    /// Returns:
    ///     A task completed when the command has been processed.
    /// </summary>
    private async Task SendChatInputAsync()
    {
        string text = _chatInputBox.Text.Trim();

        if (text.Length == 0)
        {
            return;
        }

        _chatInputBox.Clear();
        await _engine.SubmitCommandAsync(text).ConfigureAwait(true);
    }

    /// <summary>
    /// Adds a message bubble to the chat section.
    ///
    /// Args:
    ///     message: Message to show.
    /// </summary>
    private void AppendChatMessage(ConversationMessage message)
    {
        if (IsDisposed || _chatList is null)
        {
            return;
        }

        if (_chatEmptyLabel.Parent is not null)
        {
            _chatList.Controls.Remove(_chatEmptyLabel);
        }

        bool fromAssistant = message.Source is ConversationMessageSource.Assistant;
        var row = new Panel
        {
            Width = Math.Max(1, _chatViewport.ClientSize.Width),
            BackColor = Palette.Surface,
            Margin = new Padding(0, 0, 0, Palette.Unit),
            Tag = fromAssistant,
        };

        var bubble = new RoundedPanel
        {
            AutoSize = false,
            BackColor = fromAssistant ? Palette.Accent : Palette.Field,
            CornerRadius = 12,
            Margin = new Padding(0),
        };

        var text = new Label
        {
            Text = message.Text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            UseMnemonic = false,
            MaximumSize = new Size(Math.Max(220, _chatViewport.ClientSize.Width - Palette.Unit * 12), 0),
            ForeColor = Palette.TextPrimary,
            BackColor = Color.Transparent,
            Padding = new Padding(Palette.Unit * 2, Palette.Unit, Palette.Unit * 2, Palette.Unit),
            Margin = new Padding(0),
            Font = FontFactory.Create(9.25f),
        };

        bubble.Controls.Add(text);
        row.Controls.Add(bubble);
        LayoutChatRow(row);
        _chatList.Controls.Add(row);
        SetChatScrollOffset(GetMaxChatScrollOffset());
    }

    /// <summary>
    /// Rebuilds the chat surface from the engine history after the UI is recreated.
    /// </summary>
    private void ReplayChatHistory()
    {
        if (IsDisposed || _chatList is null || _chatEmptyLabel is null)
        {
            return;
        }

        _chatList.Controls.Clear();
        _chatList.Controls.Add(_chatEmptyLabel);

        foreach (ConversationMessage message in _engine.ConversationHistory)
        {
            AppendChatMessage(message);
        }
    }

    private void LayoutChatMessages()
    {
        if (_chatList is null || _chatViewport is null)
        {
            return;
        }

        _chatList.Width = Math.Max(1, _chatViewport.ClientSize.Width);

        foreach (Control control in _chatList.Controls)
        {
            if (control is Panel row)
            {
                LayoutChatRow(row);
            }
        }

        UpdateChatScroll();
    }

    private void LayoutChatRow(Panel row)
    {
        if (_chatViewport is null || row.Controls.Count == 0 || row.Controls[0] is not Panel bubble)
        {
            return;
        }

        if (bubble.Controls.Count == 0 || bubble.Controls[0] is not Label text)
        {
            return;
        }

        bool fromAssistant = row.Tag is true;
        int rowWidth = Math.Max(1, _chatViewport.ClientSize.Width);
        int maxBubbleWidth = Math.Max(220, rowWidth - Palette.Unit * 12);

        row.Width = rowWidth;
        text.MaximumSize = new Size(maxBubbleWidth, 0);
        Size preferred = text.GetPreferredSize(new Size(maxBubbleWidth, 0));
        bubble.Size = preferred;
        bubble.Location = fromAssistant
            ? new Point(Palette.Unit, 0)
            : new Point(Math.Max(Palette.Unit, rowWidth - preferred.Width - Palette.Unit), 0);
        row.Height = preferred.Height;
    }

    private void ScrollChatBy(int delta) => SetChatScrollOffset(GetChatScrollOffset() + delta);

    private void SetChatScrollOffset(int offset)
    {
        if (_chatList is null)
        {
            return;
        }

        int clamped = Math.Clamp(offset, 0, GetMaxChatScrollOffset());
        _chatList.Top = -clamped;
        UpdateChatScrollThumb(clamped);
    }

    private int GetChatScrollOffset() => _chatList is null ? 0 : Math.Max(0, -_chatList.Top);

    private int GetMaxChatScrollOffset()
    {
        if (_chatList is null || _chatViewport is null)
        {
            return 0;
        }

        return Math.Max(0, _chatList.Height - _chatViewport.ClientSize.Height);
    }

    private void UpdateChatScroll()
    {
        LayoutChatMessagesCore();
        SetChatScrollOffset(GetChatScrollOffset());
    }

    private void LayoutChatMessagesCore()
    {
        if (_chatList is null || _chatViewport is null)
        {
            return;
        }

        _chatList.Width = Math.Max(1, _chatViewport.ClientSize.Width);
    }

    private void UpdateChatScrollThumb(int offset)
    {
        if (_chatScrollTrack is null || _chatScrollThumb is null || _chatViewport is null || _chatList is null)
        {
            return;
        }

        int maxOffset = GetMaxChatScrollOffset();
        _chatScrollTrack.Visible = maxOffset > 0;

        if (maxOffset == 0)
        {
            _chatScrollThumb.Top = 0;
            _chatScrollThumb.Height = Math.Max(32, _chatScrollTrack.ClientSize.Height);
            return;
        }

        int trackHeight = Math.Max(1, _chatScrollTrack.ClientSize.Height);
        int thumbHeight = Math.Clamp(
            _chatViewport.ClientSize.Height * trackHeight / Math.Max(_chatList.Height, 1),
            36,
            trackHeight);
        int travel = Math.Max(1, trackHeight - thumbHeight);

        _chatScrollThumb.Height = thumbHeight;
        _chatScrollThumb.Top = Math.Clamp(offset * travel / maxOffset, 0, travel);
    }

    private void JumpChatScroll(int y)
    {
        if (_chatScrollThumb is null)
        {
            return;
        }

        int target = y < _chatScrollThumb.Top
            ? GetChatScrollOffset() - _chatViewport.ClientSize.Height
            : GetChatScrollOffset() + _chatViewport.ClientSize.Height;

        SetChatScrollOffset(target);
    }

    private void BeginChatThumbDrag(int startY)
    {
        if (_chatScrollThumb is null || _chatScrollTrack is null)
        {
            return;
        }

        int startScreenY = Cursor.Position.Y;
        int startOffset = GetChatScrollOffset();

        MouseEventHandler? move = null;
        MouseEventHandler? up = null;

        move = (_, _) =>
        {
            int maxOffset = GetMaxChatScrollOffset();
            int travel = Math.Max(1, _chatScrollTrack.ClientSize.Height - _chatScrollThumb.Height);
            int delta = Cursor.Position.Y - startScreenY;
            SetChatScrollOffset(startOffset + (delta * maxOffset / travel));
        };

        up = (_, _) =>
        {
            _chatScrollThumb.Capture = false;
            _chatScrollThumb.MouseMove -= move;
            _chatScrollThumb.MouseUp -= up;
        };

        _chatScrollThumb.Capture = true;
        _chatScrollThumb.MouseMove += move;
        _chatScrollThumb.MouseUp += up;
    }

    /// <summary>
    /// Reflects the assistant state in the footer.
    ///
    /// This is where a microphone failure becomes visible: instead of being announced
    /// out loud on every start, it appears as a persistent window state.
    ///
    /// Args:
    ///     state: State to display.
    /// </summary>
    private void UpdateEngineStatus(AssistantState state)
    {
        if (IsDisposed || _statusLabel is null)
        {
            return;
        }

        (string key, Color color) = state switch
        {
            AssistantState.AwaitingWakeWord => ("ui.status.idle", Palette.Listening),
            AssistantState.ListeningForCommand => ("ui.status.listening", Palette.Listening),
            AssistantState.Working => ("ui.status.thinking", Palette.Speaking),
            AssistantState.Speaking => ("ui.status.speaking", Palette.Speaking),
            AssistantState.ListeningDisabled => ("ui.status.disabled", Palette.Muted),
            AssistantState.RecognitionUnavailable => ("ui.status.unavailable", Palette.Warning),
            _ => ("ui.status.stopped", Palette.Muted),
        };

        string wakePhrase = _model.WakePhrases.Count > 0 ? _model.WakePhrases[0] : "ok laura";

        _statusLabel.Text = _localizer.Get(key, wakePhrase);
        _statusLabel.ForeColor = color;

        _hintLabel.Text = state is AssistantState.RecognitionUnavailable
            ? _localizer.Get("ui.status.unavailableHint")
            : _localizer.Get("ui.footer.hotkey");
    }

    /// <summary>
    /// Shows a temporary confirmation and then restores the engine state.
    ///
    /// Args:
    ///     message: Text to display.
    /// </summary>
    private void ShowTransientStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = Palette.Listening;

        _ = RestoreStatusAfterDelayAsync();
    }

    /// <summary>
    /// Restores the engine state in the footer after a short pause.
    ///
    /// Returns:
    ///     A task that completes after the restore.
    /// </summary>
    private async Task RestoreStatusAfterDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        if (!IsDisposed)
        {
            UpdateEngineStatus(_engine.State);
        }
    }

    // === Model/control synchronization ===

    /// <summary>
    /// Fills the controls with the values of the edit model.
    /// </summary>
    private void LoadModelIntoControls()
    {
        PopulateVoiceCombo();
        PopulateMicrophoneCombo();
        PopulateModelCombo();

        _nicknameBox.Text = _model.UserNickname;

        _rateSlider.Value = _model.Rate;
        _pitchSlider.Value = _model.Pitch;
        _volumeSlider.Value = _model.Volume;

        _recognitionToggle.Checked = _model.RecognitionEnabled;
        _noiseToggle.Checked = _model.NoiseSuppression;
        _wakePhrasesBox.Text = string.Join(Environment.NewLine, _model.WakePhrases);
        _confidenceSlider.Value = (int)Math.Round(_model.MinimumConfidence * 100);
        _timeoutSlider.Value = _model.CommandTimeoutSeconds;
        _inlineToggle.Checked = _model.AllowInlineCommand;

        _greetToggle.Checked = _model.GreetOnStartup;
        _hourlyToggle.Checked = _model.AnnounceHourly;
        _startupToggle.Checked = _model.StartWithWindows;

        _aiToggle.Checked = _model.GenerativeAiEnabled;
        _endpointBox.Text = _model.GenerativeAiEndpoint;
        _personaBox.Text = _model.GenerativeAiPersona;
    }

    /// <summary>
    /// Reads the control values back into the edit model.
    /// </summary>
    private void CommitControlsToModel()
    {
        _model.Culture = LauraSettings.DefaultCulture;

        _model.UserNickname = _nicknameBox.Text.Trim();

        _model.VoiceName = (_voiceCombo.SelectedItem as ComboItem)?.Value;
        _model.Rate = _rateSlider.Value;
        _model.Pitch = _pitchSlider.Value;
        _model.Volume = _volumeSlider.Value;

        _model.RecognitionEnabled = _recognitionToggle.Checked;
        _model.MicrophoneDeviceId = (_microphoneCombo.SelectedItem as ComboItem)?.Value;
        _model.NoiseSuppression = _noiseToggle.Checked;
        _model.WakePhrases = ParseWakePhrases(_wakePhrasesBox.Text);
        _model.MinimumConfidence = _confidenceSlider.Value / 100.0;
        _model.CommandTimeoutSeconds = _timeoutSlider.Value;
        _model.AllowInlineCommand = _inlineToggle.Checked;

        _model.GreetOnStartup = _greetToggle.Checked;
        _model.AnnounceHourly = _hourlyToggle.Checked;
        _model.StartWithWindows = _startupToggle.Checked;

        _model.GenerativeAiEnabled = _aiToggle.Checked;
        _model.GenerativeAiEndpoint = _endpointBox.Text.Trim();
        _model.GenerativeAiModel = (_modelCombo.SelectedItem as ComboItem)?.Value ?? string.Empty;
        _model.GenerativeAiPersona = _personaBox.Text.Trim();
    }

    /// <summary>
    /// Fills the voice combo with the automatic option and the current language's voices.
    /// </summary>
    private void PopulateVoiceCombo()
    {
        _voiceCombo.Items.Clear();
        _voiceCombo.SelectedIndex = -1;
        _voiceCombo.Items.Add(new ComboItem(Text_("ui.voice.automatic"), null));

        foreach (VoiceDescriptor voice in _model.GetSelectableVoices())
        {
            int index = _voiceCombo.Items.Add(new ComboItem(voice.DisplayName, voice.Name));

            if (string.Equals(voice.Name, _model.VoiceName, StringComparison.Ordinal))
            {
                _voiceCombo.SelectedIndex = index;
            }
        }

        if (_voiceCombo.SelectedIndex < 0)
        {
            _voiceCombo.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Fills the microphone combo with the available capture devices.
    /// </summary>
    private void PopulateMicrophoneCombo()
    {
        _microphoneCombo.Items.Clear();
        _microphoneCombo.SelectedIndex = -1;

        foreach (AudioDevice device in _model.GetInputDevices())
        {
            int index = _microphoneCombo.Items.Add(new ComboItem(device.Name, device.Id));

            if (string.Equals(device.Id, _model.MicrophoneDeviceId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                _microphoneCombo.SelectedIndex = index;
            }
        }

        if (_microphoneCombo.SelectedIndex < 0 && _microphoneCombo.Items.Count > 0)
        {
            _microphoneCombo.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Fills the model combo with the models the provider has actually pulled.
    ///
    /// Only installed models are offered — nothing is hard-coded. When none is
    /// installed, a placeholder is shown and the choice resolves to empty.
    /// </summary>
    private void PopulateModelCombo()
    {
        _modelCombo.Items.Clear();
        _modelCombo.SelectedIndex = -1;

        foreach (string model in _model.GetInstalledModels())
        {
            int index = _modelCombo.Items.Add(new ComboItem(model, model));

            if (string.Equals(model, _model.GenerativeAiModel, StringComparison.OrdinalIgnoreCase))
            {
                _modelCombo.SelectedIndex = index;
            }
        }

        if (_modelCombo.Items.Count == 0)
        {
            _modelCombo.Items.Add(new ComboItem(Text_("ui.ai.modelEmpty"), string.Empty));
            _modelCombo.SelectedIndex = 0;
        }
        else if (_modelCombo.SelectedIndex < 0)
        {
            // The saved model is not installed anymore; fall back to the first one.
            _modelCombo.SelectedIndex = 0;
        }
    }

    // === Helpers ===

    /// <summary>
    /// Parses the multiline wake-phrase text.
    ///
    /// Args:
    ///     text: Text box content, one phrase per line.
    ///
    /// Returns:
    ///     The non-empty phrases, trimmed.
    /// </summary>
    private static IReadOnlyList<string> ParseWakePhrases(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Tries to apply the application icon to the window.
    /// </summary>
    private void TryApplyWindowIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");

        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
    }

    /// <summary>Resolves an interface text in the active language.</summary>
    private string Text_(string key) => _localizer.Get(key);

    /// <summary>Formats a value with an explicit sign, such as "+3".</summary>
    private static string FormatSigned(int value) => value.ToString("+0;-0;0", CultureInfo.CurrentCulture);

    /// <summary>Formats a value as a percentage.</summary>
    private static string FormatPercent(int value) => $"{value}%";

    /// <summary>Formats a duration in seconds using the active translation.</summary>
    private string FormatSeconds(int value) => _localizer.Get("ui.recognition.seconds", value);

}

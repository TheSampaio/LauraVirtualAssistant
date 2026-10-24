using Laura.App.Infrastructure;
using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using NAudio.Wave;

namespace Laura.App.Views;

/// <summary>Native vertical navigation for connections, local models, voice, preferences and discovered applications.</summary>
public sealed class SetupPage : ContentPage
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyStore _keys;
    private readonly GeminiLiveClient _gemini;
    private readonly ConversationProviderRouter _router;
    private readonly LocalModelSettingsView _localSettings;
    private readonly IUiLocalizer _ui;
    private readonly IStartupRegistration _startup;
    private readonly ISpeechSynthesizer _speech;
    private readonly ILocalizer _conversation;
    private readonly WindowsApplicationCatalog _applications;
    private readonly Picker _voice, _portugueseVoice, _culture, _uiCulture, _input, _output;
    private readonly Picker _geminiVoice, _lightProvider;
    private readonly Switch _geminiEnabled;
    private readonly Entry _geminiKey = Theme.Entry(), _geminiModel = Theme.Entry();
    private readonly Label _geminiKeyState = Theme.Label("", 12, Theme.Muted);
    private readonly Entry _nickname = Theme.Entry(), _weatherLocation = Theme.Entry(), _appSearch = Theme.Entry();
    private readonly Editor _persona = new() { TextColor = Theme.Text, BackgroundColor = Theme.Field, HeightRequest = 120 };
    private readonly Switch _muted, _remember, _greeting, _hourly, _autoStart, _clapWake;
    private readonly Slider _volume, _silence, _clapSensitivity;
    private readonly int[] _inputIds, _outputIds;
    private readonly Label _result = Theme.Label("", 13, Theme.Listening);
    private readonly Label _silenceValue = Theme.Label("", 12, Theme.Muted);
    private readonly Label _volumeValue = Theme.Label("", 12, Theme.Text), _sensitivityValue = Theme.Label("", 12, Theme.Text);
    private readonly Label _appCount = Theme.Label("", 12, Theme.Muted);
    private readonly CollectionView _appList = new() { SelectionMode = SelectionMode.None };
    private readonly List<(Grid Row, View Control, bool Toggle)> _responsiveRows = [];
    private readonly Grid _body;
    private readonly Grid _tabLayout;
    private readonly Grid _workspaceHeader;
    private readonly View _brand;
    private int _activeTab;
    private bool? _compact;
    private int _configuredRows;
    private int _searchRevision;
    private readonly View?[] _pages;
    private readonly Func<View>[] _pageFactories;
    private readonly Button[] _tabs;
    private readonly ContentView _active = new();
    private readonly string _createdCulture;
    private readonly PermissionSettingsView _permissions;
    private readonly Laura.Core.Permissions.PermissionManager _permissionManager;
    private readonly ScreenshotSettingsView _screenshots;
    private readonly LegacyProviderCleanup _legacyCleanup;
    private readonly ChatGptSettingsView _chatGpt;
    private LauraSettings _baseline;
    private bool _saving;

    /// <summary>Creates lazy settings panels with a vertical navigation rail and a fixed save footer.</summary>
    public SetupPage(ISettingsService settings, IApiKeyStore keys, GeminiLiveClient gemini, LocalPackInstaller localInstaller, IUiLocalizer ui,
        IStartupRegistration startup, ISpeechSynthesizer speech, WindowsApplicationCatalog applications, ILocalizer conversation, Laura.App.Platforms.Windows.WindowsApprovalCenter approvals, ScreenshotCoordinator screenshots,
        Laura.Core.Mcp.McpServerStore mcpStore, Laura.Core.Mcp.McpRuntime mcp, ConversationProviderRouter router, LegacyProviderCleanup legacyCleanup,
        IChatGptConnection chatGpt, IChatGptBrokerSetup chatGptBrokerSetup, Laura.Core.Permissions.PermissionManager permissionManager)
    {
        using var performance = PerformanceTrace.Measure("settings.construct");
        _settings = settings; _keys = keys; _gemini = gemini; _router = router; _ui = ui; _startup = startup; _speech = speech; _applications = applications; _conversation = conversation;
        _legacyCleanup = legacyCleanup; _permissionManager = permissionManager;
        _chatGpt = new ChatGptSettingsView(chatGpt, chatGptBrokerSetup, ui, settings.Current);
        _localSettings = new LocalModelSettingsView(ui, localInstaller, settings);
        _createdCulture = ui.Culture.Name;
        LauraSettings current = settings.Current;
        _baseline = DraftBaseline(current);
        Title = ui.Get("ui.maui.setup"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        _voice = Theme.Picker(["af_heart", "af_bella", "bf_alice", "bf_emma", "bf_isabella", "bf_lily", "gb_cori"], current.LocalVoice.EnglishVoice);
        _portugueseVoice = Theme.Picker(["pt_dii", "pf_dora"], current.LocalVoice.PortugueseVoice);
        _geminiVoice = Theme.Picker(GeminiLiveOptions.Voices, current.GenerativeAi.LiveVoice);
        _lightProvider = Theme.Picker([ui.Get("ui.gemini.lightGemini"), ui.Get("ui.gemini.lightLocal")],
            current.GenerativeAi.LightProvider == LightProviderSelection.Local ? ui.Get("ui.gemini.lightLocal") : ui.Get("ui.gemini.lightGemini"));
        _geminiEnabled = Toggle(current.GenerativeAi.Enabled);
        _geminiModel.Text = current.GenerativeAi.Model; _geminiModel.Placeholder = GeminiLiveOptions.Model;
        _geminiKey.IsPassword = true; _geminiKey.Placeholder = ui.Get("ui.maui.keyPlaceholder");
        RefreshGeminiKeyState();
        _culture = Theme.Picker(["pt-BR", "en-US"], current.Culture); _uiCulture = Theme.Picker(["pt-BR", "en-US"], current.UiCulture);
        _nickname.Text = current.UserNickname; _weatherLocation.Text = current.WeatherLocation; _persona.Text = current.GenerativeAi.Persona;
        _muted = Toggle(current.MicrophoneMuted); _remember = Toggle(current.RememberConversations);
        _greeting = Toggle(current.GreetOnStartup); _hourly = Toggle(current.AnnounceHourly); _autoStart = Toggle(current.StartWithWindows);
        _clapWake = Toggle(current.ClapWakeEnabled);
        _clapSensitivity = new Slider { Minimum = 0, Maximum = 100, Value = current.ClapWakeSensitivity, MinimumTrackColor = Theme.Accent, ThumbColor = Theme.Accent };
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = current.Voice.Volume, MinimumTrackColor = Theme.Accent, ThumbColor = Theme.Accent };
        _silence = new Slider { Minimum = 300, Maximum = 2000, Value = current.GenerativeAi.SilenceDurationMs, MinimumTrackColor = Theme.Accent, ThumbColor = Theme.Accent };
        SemanticProperties.SetDescription(_volume, ui.Get("ui.voice.volume"));
        SemanticProperties.SetDescription(_silence, ui.Get("ui.maui.silence"));
        SemanticProperties.SetDescription(_clapSensitivity, ui.Get("ui.maui.clapSensitivity"));
        _silence.ValueChanged += (_, _) => UpdateSilence(); UpdateSilence();
        _volume.ValueChanged += (_, _) => _volumeValue.Text = $"{_volume.Value:F0}%";
        _clapSensitivity.ValueChanged += (_, _) => _sensitivityValue.Text = $"{_clapSensitivity.Value:F0}%";
        _volumeValue.Text = $"{_volume.Value:F0}%"; _sensitivityValue.Text = $"{_clapSensitivity.Value:F0}%";
        _inputIds = [-1, .. Enumerable.Range(0, WaveInEvent.DeviceCount)];
        _outputIds = [-1, .. Enumerable.Range(0, WaveInterop.waveOutGetNumDevs())];
        string[] inputs = [ui.Get("ui.maui.systemDefault"), .. _inputIds.Skip(1).Select(id => AudioDeviceNames.Input(id, ui.Get("ui.maui.unavailable")))];
        string[] outputs = [ui.Get("ui.maui.systemDefault"), .. _outputIds.Skip(1).Select(id => AudioDeviceNames.Output(id, ui.Get("ui.maui.unavailable")))];
        _input = Theme.Picker(inputs, inputs[Math.Max(0, Array.IndexOf(_inputIds, current.InputDevice))]);
        _output = Theme.Picker(outputs, outputs[Math.Max(0, Array.IndexOf(_outputIds, current.OutputDevice))]);
        _appSearch.Placeholder = ui.Get("ui.maui.searchApps"); _appSearch.TextChanged += async (_, _) => await SearchApplicationsAsync().ConfigureAwait(true);
        _permissions = new(settings, ui, approvals, permissionManager);
        _screenshots = new(settings, ui, screenshots);
        _pageFactories = [ConnectionPanel, () => Panel(_localSettings, Row("ui.local.englishVoice", "audio_lines", _voice),
            Row("ui.local.portugueseVoice", "audio_lines", _portugueseVoice), Hint("ui.local.voiceHelp")), VoicePanel, PreferencesPanel, ApplicationsPanel, () => Panel(_permissions), () => Panel(new McpSettingsView(mcpStore, mcp, ui))];
        _pages = new View?[_pageFactories.Length];
        string[] names = ["ui.maui.connectionTab", "ui.local.title", "ui.tab.voice", "ui.maui.preferencesTab", "ui.maui.appsTab", "ui.permissions.tab", "ui.mcp.title"];
        string[] icons = ["link", "box", "mic", "settings_2", "layout_grid", "shield_check", "network"];
        _tabs = names.Select((name, index) => ActionButton(name, icons[index], () => { SelectTab(index); return Task.CompletedTask; })).ToArray();
        _tabLayout = new Grid { RowDefinitions = new RowDefinitionCollection(Enumerable.Range(0, _tabs.Length).Select(_ => new RowDefinition(GridLength.Auto)).ToArray()), RowSpacing = 6 };
        for (int index = 0; index < _tabs.Length; index++)
        {
            var tab = _tabs[index]; tab.HorizontalOptions = LayoutOptions.Fill;
            tab.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 12);
            tab.HandlerChanged += (_, _) => { if (tab.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button native) { native.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left; } };
            _tabLayout.Add(tab, 0, index);
        }
        _result.IsVisible = false; _result.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Label.Text)) { _result.IsVisible = !string.IsNullOrEmpty(_result.Text); } };
        var footer = Theme.Stack(_result, ActionButton("ui.maui.save", "save", async () => { await SaveAsync().ConfigureAwait(true); }, true));
        _body = new Grid { ColumnDefinitions = [new(new GridLength(210)), new(GridLength.Star)], RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 18, RowSpacing = 12, Margin = new Thickness(Theme.PageInset, 0, Theme.PageInset, Theme.PageInset), HorizontalOptions = LayoutOptions.Fill };
        var navigation = new ScrollView { Content = _tabLayout, VerticalScrollBarVisibility = ScrollBarVisibility.Default };
        _body.Add(navigation, 0, 0); Grid.SetRowSpan(navigation, 2); _body.Add(_active, 1, 0); _body.Add(footer, 1, 1);
        _brand = Theme.WorkspaceBrand(ui.Get("ui.hud.subtitle"));
        _workspaceHeader = new Grid { ColumnDefinitions = [new(new GridLength(155)), new(GridLength.Star)], ColumnSpacing = 12, Padding = Theme.HeaderPadding };
        _workspaceHeader.Add(_brand);
        _workspaceHeader.Add(Theme.PageHeading(Title, ui.Get("ui.settings.subtitle"), "settings_2", ui.Get("ui.navigation.backToLaura"),
            async () => { await NavigationMotion.PopAsync(Navigation).ConfigureAwait(true); }), 1);
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], BackgroundColor = Theme.Background };
        root.Add(_workspaceHeader); root.Add(_body, 0, 1); Content = root;
        SizeChanged += (_, _) => UpdateLayout();
        SelectTab(0);
    }
    /// <summary>Gets whether the existing native controls can be reused without stale language or endpoint choices.</summary>
    public bool CanReuse => _createdCulture == _ui.Culture.Name && _inputIds.Length == WaveInEvent.DeviceCount + 1 && _outputIds.Length == WaveInterop.waveOutGetNumDevs() + 1;
    /// <summary>Restores persisted values when reopening cached controls, discarding unsaved drafts as before.</summary>
    public void ReloadFromSettings()
    {
        var current = _settings.Current;
        _baseline = DraftBaseline(current);
        _chatGpt.Reload(current);
        _localSettings.Reload(current);
        _geminiEnabled.IsToggled = current.GenerativeAi.Enabled; _geminiModel.Text = current.GenerativeAi.Model;
        _geminiVoice.SelectedItem = current.GenerativeAi.LiveVoice;
        _lightProvider.SelectedIndex = current.GenerativeAi.LightProvider == LightProviderSelection.Local ? 1 : 0;
        _geminiKey.Text = ""; _geminiKey.IsPassword = true; RefreshGeminiKeyState();
        _result.Text = "";
        _nickname.Text = current.UserNickname; _weatherLocation.Text = current.WeatherLocation; _persona.Text = current.GenerativeAi.Persona;
        _permissions.Reload(current.Permissions);
        _screenshots.Reload(current.Screenshots);
        _voice.SelectedItem = current.LocalVoice.EnglishVoice; _portugueseVoice.SelectedItem = current.LocalVoice.PortugueseVoice;
        _culture.SelectedItem = current.Culture; _uiCulture.SelectedItem = current.UiCulture;
        _muted.IsToggled = current.MicrophoneMuted; _remember.IsToggled = current.RememberConversations;
        _greeting.IsToggled = current.GreetOnStartup; _hourly.IsToggled = current.AnnounceHourly; _autoStart.IsToggled = current.StartWithWindows;
        _clapWake.IsToggled = current.ClapWakeEnabled; _clapSensitivity.Value = current.ClapWakeSensitivity;
        _volume.Value = current.Voice.Volume; _silence.Value = current.GenerativeAi.SilenceDurationMs;
        for (int index = 1; index < _inputIds.Length; index++) { string name = AudioDeviceNames.Input(_inputIds[index], _ui.Get("ui.maui.unavailable")); if (_input.Items[index] != name) { _input.Items[index] = name; } }
        for (int index = 1; index < _outputIds.Length; index++) { string name = AudioDeviceNames.Output(_outputIds[index], _ui.Get("ui.maui.unavailable")); if (_output.Items[index] != name) { _output.Items[index] = name; } }
        _input.SelectedIndex = Math.Max(0, Array.IndexOf(_inputIds, current.InputDevice)); _output.SelectedIndex = Math.Max(0, Array.IndexOf(_outputIds, current.OutputDevice));
        _appSearch.Text = ""; SelectTab(0);
    }
    /// <summary>Selects a single tab while preserving unsaved values.</summary>
    public void SelectTab(int index)
    {
        if (index < 0 || index >= _pages.Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
        if (_activeTab == index && _active.Content is not null) { return; }
        _activeTab = index;
        _active.Content = _pages[index] ??= _pageFactories[index]();
        UpdateLayout();
        if (index == 4) { RefreshApplications(); }
        for (int item = 0; item < _tabs.Length; item++)
        {
            Theme.SetSelected(_tabs[item], item == index, _tabs[item].Text + (item == index ? " Â· " + _ui.Get("ui.navigation.selected") : ""));
        }
    }
    /// <summary>Creates a persistent scroll viewport inside a fixed rounded surface.</summary>
    private static View Panel(params View[] children)
    {
        var stack = Theme.Stack(children); stack.Spacing = 8; stack.Padding = new Thickness(0, 0, 16, 0);
        var card = Theme.Card(new ScrollView { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Default }, 24);
        card.Padding = new Thickness(24, 24, 12, 24); return card;
    }
    /// <summary>Creates an accent switch.</summary>
    private static Switch Toggle(bool value)
    {
        var toggle = new Switch { IsToggled = value, OnColor = Theme.Accent, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, WidthRequest = 52, MinimumHeightRequest = Theme.TargetSize };
        toggle.HandlerChanged += (_, _) =>
        {
            if (toggle.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ToggleSwitch native) { native.MinWidth = 0; native.Width = 52; }
        };
        return toggle;
    }
    /// <summary>Creates a labeled native action with a bundled SVG and a consistent hit target.</summary>
    private Button ActionButton(string key, string icon, Func<Task> action, bool primary = false)
    {
        return Theme.ActionButton(_ui.Get(key), icon, action, primary);
    }
    /// <summary>Creates a translated icon action for pointer and keyboard access.</summary>
    private ImageButton ActionIcon(string icon, string key, Func<Task> action)
    { var button = Theme.IconButton(icon, action); Theme.DescribeAction(button, _ui.Get(key)); return button; }
    /// <summary>Creates a restrained red icon badge for a setting group.</summary>
    private static View Badge(string icon) => new Border
    {
        Content = Theme.Icon(icon, icon != "shield_check", 22), WidthRequest = 44, HeightRequest = 44,
        BackgroundColor = Theme.AssistantSurface, Stroke = Theme.Line,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, VerticalOptions = LayoutOptions.Center
    };
    /// <summary>Creates a field row which stacks wide controls on compact windows.</summary>
    private View Row(string key, string icon, View control, string? description = null)
    {
        bool toggle = control is Switch;
        var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(new GridLength(250)), new(GridLength.Star)], ColumnSpacing = 16, RowSpacing = 10, Padding = new Thickness(0, 6), MinimumHeightRequest = 60 };
        var title = Theme.Label(_ui.Get(key), 14); title.VerticalOptions = LayoutOptions.Center;
        View text = description is null ? title : Theme.Stack(title, Theme.Label(_ui.Get(description), 12, Theme.Muted));
        if (text is VerticalStackLayout stack) { stack.Spacing = 4; }
        text.VerticalOptions = LayoutOptions.Center;
        row.Add(Badge(icon)); row.Add(text, 1); row.Add(control, 2);
        control.VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(control, _ui.Get(key));
        _responsiveRows.Add((row, control, toggle)); return row;
    }
    /// <summary>Displays a real slider value next to the adjustable track.</summary>
    private static View Meter(Slider slider, Label value)
    {
        slider.MaximumTrackColor = Theme.Line; value.WidthRequest = 68; value.HorizontalTextAlignment = TextAlignment.End; value.VerticalOptions = LayoutOptions.Center;
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12, MinimumHeightRequest = 44 };
        grid.Add(slider); grid.Add(value, 1); return grid;
    }
    /// <summary>Creates an inline information hint without additional decorative borders.</summary>
    private View Hint(string key)
    {
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
        grid.Add(Theme.Icon("info", true)); grid.Add(Theme.Label(_ui.Get(key), 12, Theme.Muted), 1);
        return grid;
    }
    /// <summary>Creates an accessible information callout using the application palette.</summary>
    private View Notice(string key)
    { var card = Theme.Card(Hint(key), 14); card.BackgroundColor = Theme.Field; return card; }
    /// <summary>Shows the authorized ChatGPT account and shared provider selection.</summary>
    private View ConnectionPanel() => Panel(Row("ui.local.routing", "brain", _chatGpt.SelectionControl),
        Row("ui.gemini.lightProvider", "brain", _lightProvider),
        Theme.Label("Gemini Live", 22), Row("ui.ai.enabled", "globe", _geminiEnabled),
        Row("ui.maui.key", "shield_check", _geminiKey), _geminiKeyState,
        Theme.ActionButton(_ui.Get("ui.settings.revealKey"), "eye", () => { _geminiKey.IsPassword = !_geminiKey.IsPassword; return Task.CompletedTask; }),
        Row("ui.ai.model", "box", _geminiModel), Row("ui.gemini.voice", "audio_lines", _geminiVoice),
        Theme.ActionButton(_ui.Get("ui.maui.removeKey"), "trash", RemoveGeminiKeyAsync),
        Hint("ui.maui.cloudPrivacy"), _chatGpt,
        ActionButton("ui.legacy.cleanup", "trash", CleanLegacyModelsAsync));
    /// <summary>Updates only protected credential metadata; the secret never enters a label.</summary>
    private void RefreshGeminiKeyState() => _geminiKeyState.Text = _ui.Get("ui.credential." + _keys.Inspect());
    /// <summary>Removes the saved AI Studio key after draining its active Live session.</summary>
    private async Task RemoveGeminiKeyAsync()
    { await _gemini.ResetSessionAsync(() => { _keys.Save(""); return Task.CompletedTask; }).ConfigureAwait(true); _geminiKey.Text = ""; RefreshGeminiKeyState(); }
    /// <summary>Builds voice and local wake controls with their actual units and privacy states.</summary>
    private View VoicePanel() => Panel(
        Row("ui.maui.input", "mic", _input), Row("ui.maui.output", "headphones", _output),
        Row("ui.voice.volume", "volume_2", Meter(_volume, _volumeValue)),
        Row("ui.maui.silence", "clock", Meter(_silence, _silenceValue)),
        Row("ui.maui.muted", "mic_off", _muted), Row("ui.maui.clapWake", "hand", _clapWake),
        Notice("ui.maui.clapPrivacy"), Row("ui.maui.clapSensitivity", "activity", Meter(_clapSensitivity, _sensitivityValue)),
        Hint("ui.maui.listeningHelp"), ActionButton("ui.maui.savePreview", "play", PreviewAsync));
    /// <summary>Builds independent language and assistant behavior preferences.</summary>
    private View PreferencesPanel() => Panel(
        Row("ui.maui.uiLanguage", "globe", _uiCulture, "ui.settings.uiLanguageHelp"),
        Row("ui.maui.language", "message_square", _culture, "ui.settings.languageHelp"),
        Row("ui.maui.nickname", "user_round", _nickname, "ui.settings.nicknameHelp"),
        Row("ui.maui.weatherLocation", "cloud", _weatherLocation, "ui.settings.weatherLocationHelp"),
        Row("ui.memory.remember", "message_square", _remember, "ui.settings.rememberHelp"),
        Row("ui.maui.greeting", "play", _greeting, "ui.settings.greetingHelp"),
        Row("ui.maui.hourly", "clock", _hourly, "ui.settings.hourlyHelp"),
        Row("ui.maui.autostart", "power", _autoStart, "ui.settings.autostartHelp"),
        Row("ui.ai.persona", "star", _persona, "ui.settings.personaHelp"), _screenshots);
    /// <summary>Keeps the virtualized application inventory in a finite native viewport.</summary>
    private View ApplicationsPanel()
    {
        var search = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8 };
        search.Add(Theme.Icon("search")); search.Add(_appSearch, 1);
        search.Add(ActionIcon("x", "ui.settings.clearSearch", () => { _appSearch.Text = ""; return Task.CompletedTask; }), 2);
        search.Add(ActionIcon("refresh_cw", "ui.maui.refreshApps", RefreshApplicationsAsync), 3);
        _appList.ItemTemplate = new DataTemplate(ApplicationRow);
        _appList.EmptyView = Theme.Label(_ui.Get("ui.settings.noApps"), 14, Theme.Muted);
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)], RowSpacing = 12 };
        grid.Add(Theme.Label(_ui.Get("ui.maui.appsIntro"), 13, Theme.Muted)); grid.Add(search, 0, 1); grid.Add(_appCount, 0, 2); grid.Add(_appList, 0, 3);
        return Theme.Card(grid, 24);
    }
    /// <summary>Creates a recycled application row with actual names and a safe path-copy action.</summary>
    private object ApplicationRow()
    {
        var name = Theme.Label("", 14); name.MaxLines = 1; name.LineBreakMode = LineBreakMode.TailTruncation; name.SetBinding(Label.TextProperty, nameof(InstalledApplication.Name));
        var path = Theme.Label("", 11, Theme.Muted); path.MaxLines = 1; path.LineBreakMode = LineBreakMode.MiddleTruncation; path.SetBinding(Label.TextProperty, nameof(InstalledApplication.Target));
        var text = Theme.Stack(name, path); text.Spacing = 4; text.VerticalOptions = LayoutOptions.Center;
        var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        row.Add(Theme.Icon("layout_grid", true, 24)); row.Add(text, 1);
        row.Add(ActionIcon("copy", "ui.settings.copyPath", async () =>
        { if (row.BindingContext is InstalledApplication app) { await Clipboard.Default.SetTextAsync(app.Target).ConfigureAwait(true); _result.Text = _ui.Get("ui.settings.copied"); } }), 2);
        var card = Theme.Card(row, 8); card.BackgroundColor = Theme.Field;
        card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = Theme.ControlRadius };
        return new ContentView { Content = card, Padding = new Thickness(0, 0, 6, 6) };
    }
    /// <summary>Reflows only at the layout breakpoint so native controls retain focus and scroll ownership.</summary>
    private void UpdateLayout()
    {
        if (Width <= 0) { return; }
        int railWidth = Width < 900 ? 148 : 210;
        if (_body.ColumnDefinitions[0].Width.Value != railWidth) { _body.ColumnDefinitions[0].Width = new GridLength(railWidth); }
        bool brandVisible = Width >= 1360;
        if (_brand.IsVisible != brandVisible || _workspaceHeader.ColumnDefinitions[0].Width.Value != (brandVisible ? 155 : 0))
        {
            _brand.IsVisible = brandVisible;
            _workspaceHeader.ColumnDefinitions[0].Width = new GridLength(brandVisible ? 155 : 0);
            _workspaceHeader.ColumnSpacing = brandVisible ? 12 : 0;
        }
        bool compact = Width < 900;
        if (_compact == compact && _configuredRows == _responsiveRows.Count) { return; }
        int firstRow = _compact == compact ? _configuredRows : 0;
        _compact = compact;
        for (int index = firstRow; index < _responsiveRows.Count; index++)
        {
            var (row, control, toggle) = _responsiveRows[index];
            row.ColumnDefinitions = compact || toggle ? [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] : [new(GridLength.Auto), new(new GridLength(250)), new(GridLength.Star)];
            bool stack = compact && !toggle || control is Editor;
            row.RowDefinitions = stack ? [new(GridLength.Auto), new(GridLength.Auto)] : [new(GridLength.Auto)];
            Grid.SetRow(control, stack ? 1 : 0); Grid.SetColumn(control, stack ? 1 : 2); Grid.SetColumnSpan(control, stack ? 2 : 1);
        }
        _configuredRows = _responsiveRows.Count;
    }
    /// <summary>Displays speech-end delay in milliseconds.</summary>
    private void UpdateSilence() => _silenceValue.Text = $"{_silence.Value:F0} ms";
    /// <summary>Refreshes application discovery in the background.</summary>
    private async Task RefreshApplicationsAsync() { await _applications.RefreshAsync().ConfigureAwait(true); RefreshApplications(); }
    /// <summary>Shows names and targets that Laura can actually launch.</summary>
    private void RefreshApplications()
    {
        string query = _appSearch.Text?.Trim() ?? "";
        var apps = _applications.Applications.Where(app => query.Length == 0 || app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || app.Target.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _appList.ItemsSource = apps;
        _appCount.Text = _ui.Get("ui.settings.appCount", apps.Length, _applications.Applications.Count);
    }
    /// <summary>Debounces native inventory filtering without holding up navigation or typing.</summary>
    private async Task SearchApplicationsAsync()
    {
        int revision = ++_searchRevision;
        await Task.Delay(180).ConfigureAwait(true);
        if (revision == _searchRevision && _activeTab == 4) { RefreshApplications(); }
    }
    /// <summary>Gets the actual number of settings tabs, including native MCP configuration.</summary>
    public int TabCount => _pages.Length;
    /// <summary>Verifies native tab geometry and draft preservation without cloud or microphone access.</summary>
    public void VerifySettingsForSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--permissions-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Settings verification requires isolated UI smoke mode."); }
        if (_body.Width > Width || _active.Height < 100 || _tabs.Any(tab => tab.Width < Theme.TargetSize || tab.Height < Theme.TargetSize))
        { throw new InvalidOperationException("Settings content or tab targets do not fit the native window."); }
        foreach (var (_, control, toggle) in _responsiveRows.Where(item => item.Row.Handler is not null && item.Row.IsVisible))
        {
            if (!toggle && control.Width is > 0 and < 160) { throw new InvalidOperationException("A settings field was compressed below its readable width."); }
        }
        if (_activeTab == 4)
        {
            if (_appList.Height < 80 || _appList.Width < 160) { throw new InvalidOperationException("The application inventory has no usable viewport."); }
            if (_appList.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list && list.Items.Count > 0 && list.ContainerFromIndex(0) is null)
            { throw new InvalidOperationException("The application inventory did not realize its first visible row."); }
        }
        if (_activeTab == 5) { _permissions.VerifyForSmoke(); }
    }
    /// <summary>Exercises tab preservation before the native layout verification begins.</summary>
    public void VerifySettingsDraftForSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Settings verification requires isolated UI smoke mode."); }
        string? nickname = _nickname.Text;
        _nickname.Text = "Settings smoke draft";
        int selected = _activeTab; SelectTab((selected + 1) % _pages.Length); SelectTab(selected);
        if (_nickname.Text != "Settings smoke draft") { throw new InvalidOperationException("Switching settings tabs discarded the draft."); }
        _nickname.Text = nickname;
    }
    /// <summary>Checks that lower settings remain reachable in the same native scroll viewport.</summary>
    public async Task VerifySettingsScrollForSmokeAsync(bool bottom, double fraction = 1)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Settings verification requires isolated UI smoke mode."); }
        if (_pages[_activeTab] is Border { Content: ScrollView scroll })
        {
            double extent = Math.Max(0, scroll.ContentSize.Height - scroll.Height);
            double target = bottom ? extent * Math.Clamp(fraction, 0, 1) : 0;
            // MAUI does not always emit completion for a ChangeView that leaves the offset unchanged.
            if (Math.Abs(scroll.ScrollY - target) > 1)
            { await scroll.ScrollToAsync(0, target, false).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true); }
            await Task.Delay(150).ConfigureAwait(true);
            if (bottom && extent > 10 && scroll.ScrollY < 1) { throw new InvalidOperationException("The settings scroll viewport cannot reach its lower controls."); }
        }
    }
    /// <summary>Offers explicit cleanup of verified Laura-owned former model files without removing shared or edited artifacts.</summary>
    private async Task CleanLegacyModelsAsync()
    {
        if (!await Laura.App.Platforms.Windows.WindowsDialogs.ConfirmAsync(_ui.Get("ui.legacy.cleanup"), _ui.Get("ui.legacy.cleanupHelp"),
            _ui.Get("ui.maui.delete"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { return; }
        int removed = await Task.Run(() => _legacyCleanup.CleanModelsAsync()).ConfigureAwait(true);
        _result.Text = string.Format(_ui.Culture, _ui.Get("ui.legacy.cleanupResult"), removed);
    }
    /// <summary>Serializes save gestures so credential validation and writes cannot overlap.</summary>
    private async Task<bool> SaveAsync()
    {
        if (_saving) { return false; }
        _saving = true;
        try { return await SaveCoreAsync().ConfigureAwait(true); }
        finally { _saving = false; }
    }

    /// <summary>Saves the captured draft, preserving unrelated concurrent changes and provider sessions.</summary>
    private async Task<bool> SaveCoreAsync()
    {
        LauraSettings baseline = _baseline;
        LauraSettings draft = CaptureDraft();
        string? candidate = string.IsNullOrWhiteSpace(_geminiKey.Text) ? null : _geminiKey.Text.Trim();
        bool activateFullAccess = draft.Permissions.Mode == Laura.Core.Permissions.ApprovalMode.FullAccess
            && _permissionManager.CurrentMode != Laura.Core.Permissions.ApprovalMode.FullAccess;
        if (activateFullAccess && !await Laura.App.Platforms.Windows.WindowsConfirmation.AskAsync(
            _ui.Get("ui.permissions.fullAccessWarning"), _ui.Get("ui.permissions.fullAccessConfirm"),
            _ui.Get("ui.permissions.fullAccessCancel"), CancellationToken.None).ConfigureAwait(true))
        { return false; }
        if (candidate is not null)
        {
            _result.Text = _ui.Get("ui.maui.validatingKey");
            using var validation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await _gemini.ValidateCredentialAsync(candidate, draft.GenerativeAi, draft.Culture, validation.Token).ConfigureAwait(true); }
            catch (Exception error) when (error is InvalidOperationException or System.Net.WebSockets.WebSocketException or IOException or OperationCanceledException)
            { _result.Text = _ui.Get("ui.maui.invalidKey"); return false; }
        }
        LauraSettings merged = SettingsDraft.Merge(baseline, draft, _settings.Current);
        bool resetConversation = candidate is not null || merged.GenerativeAi != _settings.Current.GenerativeAi
            || merged.ProviderSelection != _settings.Current.ProviderSelection || merged.LocalModel != _settings.Current.LocalModel
            || merged.Voice != _settings.Current.Voice || merged.InputDevice != _settings.Current.InputDevice
            || merged.OutputDevice != _settings.Current.OutputDevice || merged.Culture != _settings.Current.Culture;
        LauraSettings updated = merged;
        // Persist captured edits against the latest serialized state.
        async Task PersistAsync()
        {
            if (candidate is not null) { _keys.Save(candidate); }
            updated = await _settings.UpdateAsync(current => SettingsDraft.Merge(baseline, draft, current)).ConfigureAwait(false);
        }
        if (resetConversation) { await _router.ResetSessionAsync(PersistAsync).ConfigureAwait(true); }
        else { await PersistAsync().ConfigureAwait(true); }
        if (activateFullAccess && updated.Permissions.Mode == Laura.Core.Permissions.ApprovalMode.FullAccess) { _permissionManager.ActivateFullAccess(); }
        else if (updated.Permissions.Mode != Laura.Core.Permissions.ApprovalMode.FullAccess) { _permissionManager.DeactivateFullAccess(); }
        _permissions.RefreshModeStatus();
        _baseline = draft;
        if (candidate is not null && _geminiKey.Text?.Trim() == candidate) { _geminiKey.Text = ""; }
        RefreshGeminiKeyState();
        _ui.SetCulture(updated.ResolveUiCulture());
        if (_startup.IsEnabled() != updated.StartWithWindows) { _startup.SetEnabled(updated.StartWithWindows); }
        _result.Text = _ui.Get("ui.maui.saved"); return true;
    }

    /// <summary>Normalizes displayed defaults for change detection without rewriting untouched persisted model choices.</summary>
    private static LauraSettings DraftBaseline(LauraSettings current) => current;

    /// <summary>Captures native fields before asynchronous validation can yield to further edits.</summary>
    private LauraSettings CaptureDraft() => _baseline with
        {
            ChatGpt = _chatGpt.Options, ProviderSelection = _chatGpt.Selection,
            LocalModel = _localSettings.Options, LocalVoice = _localSettings.VoiceOptions with { EnglishVoice = _voice.SelectedItem as string ?? "af_heart",
                PortugueseVoice = _portugueseVoice.SelectedItem as string ?? "pt_dii" },
            Culture = _culture.SelectedItem as string ?? "pt-BR", UiCulture = _uiCulture.SelectedItem as string ?? "pt-BR",
            UserNickname = _nickname.Text ?? "", WeatherLocation = _weatherLocation.Text ?? "", MicrophoneMuted = _muted.IsToggled, RememberConversations = _remember.IsToggled,
            ClapWakeEnabled = _clapWake.IsToggled, ClapWakeSensitivity = (int)_clapSensitivity.Value,
            InputDevice = _inputIds[Math.Max(0, _input.SelectedIndex)], OutputDevice = _outputIds[Math.Max(0, _output.SelectedIndex)],
            GreetOnStartup = _greeting.IsToggled, AnnounceHourly = _hourly.IsToggled, StartWithWindows = _autoStart.IsToggled,
            Permissions = _permissions.Options, Screenshots = _screenshots.Options,
            Voice = _baseline.Voice with { Volume = (int)_volume.Value },
            GenerativeAi = _baseline.GenerativeAi with { Enabled = _geminiEnabled.IsToggled, Provider = "gemini", Model = _geminiModel.Text ?? "",
                LiveVoice = _geminiVoice.SelectedItem as string ?? "Aoede",
                LightProvider = _lightProvider.SelectedIndex == 1 ? LightProviderSelection.Local : LightProviderSelection.Gemini,
                Persona = (_persona.Text ?? "").ReplaceLineEndings(" "), ContinuousListening = true, SilenceDurationMs = (int)_silence.Value }
        };
    /// <summary>Exercises the actual Save path when only the text-provider selection changes.</summary>
    internal async Task VerifyProviderIsolationForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated native diagnostics only."); }
        _permissions.VerifyForSmoke();
        var original = _settings.Current.GenerativeAi;
        var chatGpt = _settings.Current.ChatGpt;
        _chatGpt.Reload(_settings.Current with { ProviderSelection = ConversationProviderSelection.ChatGpt });
        if (!await SaveAsync().ConfigureAwait(true) || _settings.Current.GenerativeAi != original || _settings.Current.ChatGpt != chatGpt)
        { throw new InvalidOperationException("Saving ChatGPT selection changed shared conversation settings."); }
    }
    /// <summary>Previews the selected local feminine voice after applying language settings.</summary>
    private async Task PreviewAsync()
    {
        if (!await SaveAsync().ConfigureAwait(true)) { return; }
        if (_speech.GetAvailableVoices().Count == 0) { _result.Text = _ui.Get("ui.local.voiceUnavailable"); return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        string preview = _conversation.Get("ui.voice.previewText");
        await _speech.SpeakAsync(new SpeechRequest(preview, _settings.Current.Voice, _settings.Current.Culture), timeout.Token).ConfigureAwait(true);
    }
    internal async Task SaveSettingsForPermissionSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--permissions-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated permission diagnostics only."); }
        if (!await SaveAsync().ConfigureAwait(true)) { throw new InvalidOperationException("Security settings could not be saved."); }
    }

}

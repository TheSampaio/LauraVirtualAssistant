using System.Text.Json;
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

/// <summary>Tabbed Gemini settings for connection, natural voice, preferences and discovered applications.</summary>
public sealed class SetupPage : ContentPage
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyStore _keys;
    private readonly GeminiLiveClient _live;
    private readonly OpenAiConversationEngine _gpt;
    private readonly OpenAiApiKeyStore _openAiKeys;
    private readonly ConversationProviderRouter _router;
    private readonly Picker _chatProvider;
    private readonly Entry _openAiKey = Theme.Entry();
    private readonly ProviderModelPicker _openAiModel, _model;
    private readonly IUiLocalizer _ui;
    private readonly IStartupRegistration _startup;
    private readonly ISpeechSynthesizer _speech;
    private readonly ILocalizer _conversation;
    private readonly WindowsApplicationCatalog _applications;
    private readonly Picker _voice, _culture, _uiCulture, _input, _output;
    private readonly Entry _key = Theme.Entry(), _nickname = Theme.Entry(), _appSearch = Theme.Entry();
    private readonly Editor _persona = new() { TextColor = Theme.Text, BackgroundColor = Theme.Field, HeightRequest = 120 };
    private readonly Switch _muted, _remember, _greeting, _hourly, _autoStart, _clapWake;
    private readonly Slider _volume, _silence, _clapSensitivity;
    private readonly int[] _inputIds, _outputIds;
    private readonly Label _keyState = Theme.Label("", 12, Theme.Muted), _result = Theme.Label("", 13, Theme.Listening);
    private readonly Label _silenceValue = Theme.Label("", 12, Theme.Muted);
    private readonly Label _volumeValue = Theme.Label("", 12, Theme.Text), _sensitivityValue = Theme.Label("", 12, Theme.Text);
    private readonly Label _providerState = Theme.Label("", 12, Theme.Muted), _appCount = Theme.Label("", 12, Theme.Muted);
    private readonly ImageButton _revealKey;
    private readonly CollectionView _appList = new() { SelectionMode = SelectionMode.None };
    private readonly List<(Grid Row, View Control, bool Toggle)> _responsiveRows = [];
    private readonly Grid _body;
    private readonly Grid _tabLayout;
    private readonly Grid _workspaceHeader;
    private readonly View _brand;
    private int _activeTab;
    private bool? _compact;
    private int _searchRevision;
    private readonly View[] _pages;
    private readonly Button[] _tabs;
    private readonly ContentView _active = new();
    private readonly string _createdCulture;
    private readonly PermissionSettingsView _permissions;
    private readonly LocalModelSettingsView _localModels;
    private readonly Switch _cloudFallback = new() { OnColor = Theme.Accent };
    private View? _localConnection, _cloudConnection;
    private readonly ScreenshotSettingsView _screenshots;

    /// <summary>Creates independent tabs with a fixed save footer and no offline provider controls.</summary>
    public SetupPage(ISettingsService settings, IApiKeyStore keys, GeminiLiveClient live, IUiLocalizer ui,
        IStartupRegistration startup, ISpeechSynthesizer speech, WindowsApplicationCatalog applications, ILocalizer conversation, Laura.App.Platforms.Windows.WindowsApprovalCenter approvals, ScreenshotCoordinator screenshots,
        Laura.Core.Mcp.McpServerStore mcpStore, Laura.Core.Mcp.McpRuntime mcp, OpenAiConversationEngine gpt, OpenAiApiKeyStore openAiKeys, ConversationProviderRouter router, IProviderModelCatalog modelCatalogue, ILocalModelManager localModels, ILocalInferenceEngine localInference, IModelHardwareProbe hardware)
    {
        using var performance = PerformanceTrace.Measure("settings.construct");
        _settings = settings; _keys = keys; _live = live; _ui = ui; _startup = startup; _speech = speech; _applications = applications; _conversation = conversation;
        _gpt = gpt; _openAiKeys = openAiKeys; _router = router;
        _model = new ProviderModelPicker(modelCatalogue, ui, "gemini", () => string.IsNullOrWhiteSpace(_key.Text) ? _keys.Read() : _key.Text.Trim());
        _openAiModel = new ProviderModelPicker(modelCatalogue, ui, "openai", () => string.IsNullOrWhiteSpace(_openAiKey.Text) ? _openAiKeys.Read() : _openAiKey.Text.Trim());
        _createdCulture = ui.Culture.Name;
        LauraSettings current = settings.Current;
        _chatProvider = Theme.Picker(["Gemini Live", "GPT · OpenAI", ui.Get("ui.models.localTitle"), ui.Get("ui.models.localFirst")], "");
        _chatProvider.SelectedIndex = current.GenerativeAi.Provider switch { "gemini" => 0, "openai" => 1, "local" => 2, _ => 3 };
        _localModels = new LocalModelSettingsView(localModels, localInference, settings, ui, hardware); _cloudFallback.IsToggled = current.GenerativeAi.AllowCloudFallback;
        _chatProvider.SelectedIndexChanged += (_, _) => RefreshProviderPanels();
        _cloudFallback.Toggled += (_, _) => RefreshProviderPanels();
        _openAiKey.IsPassword = true; _openAiKey.Placeholder = string.IsNullOrWhiteSpace(openAiKeys.Read()) ? "OpenAI API key" : "••••••••••••••••";
        _openAiModel.Text = current.GenerativeAi.OpenAiModel;
        Title = ui.Get("ui.maui.setup"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        _voice = Theme.Picker(GeminiLiveOptions.Voices, current.GenerativeAi.LiveVoice);
        _culture = Theme.Picker(["pt-BR", "en-US"], current.Culture); _uiCulture = Theme.Picker(["pt-BR", "en-US"], current.UiCulture);
        _model.Text = string.IsNullOrWhiteSpace(current.GenerativeAi.Model) ? GeminiLiveOptions.Model : current.GenerativeAi.Model;
        _nickname.Text = current.UserNickname; _persona.Text = current.GenerativeAi.Persona;
        _key.IsPassword = true; _key.Placeholder = ui.Get("ui.maui.keyPlaceholder");
        SemanticProperties.SetDescription(_key, ui.Get("ui.maui.key"));
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
        _revealKey = ActionIcon("eye", "ui.settings.revealKey", () =>
        {
            _key.IsPassword = !_key.IsPassword;
            _revealKey!.Source = Theme.IconSource(_key.IsPassword ? "eye" : "eye_off");
            Theme.DescribeAction(_revealKey, _ui.Get(_key.IsPassword ? "ui.settings.revealKey" : "ui.settings.hideKey"));
            return Task.CompletedTask;
        });
        _key.TextChanged += (_, _) => _revealKey.IsEnabled = !string.IsNullOrWhiteSpace(_key.Text);
        _revealKey.IsEnabled = false;
        _permissions = new(settings, ui, approvals);
        _screenshots = new(settings, ui, screenshots);
        _pages = [ConnectionPanel(), VoicePanel(), PreferencesPanel(), ApplicationsPanel(), Panel(_permissions), Panel(new McpSettingsView(mcpStore, mcp, ui))];
        string[] names = ["ui.maui.connectionTab", "ui.tab.voice", "ui.maui.preferencesTab", "ui.maui.appsTab", "ui.permissions.tab", "ui.mcp.title"];
        string[] icons = ["link", "mic", "settings_2", "layout_grid", "shield_check", "network"];
        _tabs = names.Select((name, index) => ActionButton(name, icons[index], () => { SelectTab(index); return Task.CompletedTask; })).ToArray();
        _tabLayout = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8, RowSpacing = 8 };
        for (int index = 0; index < _tabs.Length; index++) { _tabLayout.Add(_tabs[index], index); }
        _result.IsVisible = false; _result.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Label.Text)) { _result.IsVisible = !string.IsNullOrEmpty(_result.Text); } };
        var footer = Theme.Stack(_result, ActionButton("ui.maui.save", "save", async () => { await SaveAsync().ConfigureAwait(true); }, true));
        _body = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 16, Margin = new Thickness(Theme.PageInset, 0, Theme.PageInset, Theme.PageInset), HorizontalOptions = LayoutOptions.Center };
        _body.Add(_tabLayout); _body.Add(_active, 0, 1); _body.Add(footer, 0, 2);
        _brand = Theme.WorkspaceBrand(ui.Get("ui.hud.subtitle"));
        _workspaceHeader = new Grid { ColumnDefinitions = [new(new GridLength(155)), new(GridLength.Star)], ColumnSpacing = 12, Padding = Theme.HeaderPadding };
        _workspaceHeader.Add(_brand);
        _workspaceHeader.Add(Theme.PageHeading(Title, ui.Get("ui.settings.subtitle"), "settings_2", ui.Get("ui.navigation.backToLaura"),
            async () => { await NavigationMotion.PopAsync(Navigation).ConfigureAwait(true); }), 1);
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], BackgroundColor = Theme.Background };
        root.Add(_workspaceHeader); root.Add(_body, 0, 1); Content = root;
        SizeChanged += (_, _) => UpdateLayout();
        SelectTab(0); RefreshKeyState();
    }
    /// <summary>Gets whether the existing native controls can be reused without stale language or endpoint choices.</summary>
    public bool CanReuse => _createdCulture == _ui.Culture.Name && _inputIds.Length == WaveInEvent.DeviceCount + 1 && _outputIds.Length == WaveInterop.waveOutGetNumDevs() + 1;
    /// <summary>Restores persisted values when reopening cached controls, discarding unsaved drafts as before.</summary>
    public void ReloadFromSettings()
    {
        var current = _settings.Current;
        _openAiKey.Text = ""; _openAiModel.Text = current.GenerativeAi.OpenAiModel;
        _chatProvider.SelectedIndex = current.GenerativeAi.Provider switch { "gemini" => 0, "openai" => 1, "local" => 2, _ => 3 };
        _localModels.Reload(current.GenerativeAi.LocalModel); _cloudFallback.IsToggled = current.GenerativeAi.AllowCloudFallback;
        _openAiKey.Placeholder = string.IsNullOrWhiteSpace(_openAiKeys.Read()) ? "OpenAI API key" : "••••••••••••••••";
        _key.Text = ""; _key.IsPassword = true; _revealKey.Source = Theme.IconSource("eye");
        Theme.DescribeAction(_revealKey, _ui.Get("ui.settings.revealKey")); _result.Text = "";
        _nickname.Text = current.UserNickname; _persona.Text = current.GenerativeAi.Persona;
        _permissions.Reload(current.Permissions);
        _screenshots.Reload(current.Screenshots);
        _model.Text = string.IsNullOrWhiteSpace(current.GenerativeAi.Model) ? GeminiLiveOptions.Model : current.GenerativeAi.Model;
        _voice.SelectedItem = current.GenerativeAi.LiveVoice; _culture.SelectedItem = current.Culture; _uiCulture.SelectedItem = current.UiCulture;
        _muted.IsToggled = current.MicrophoneMuted; _remember.IsToggled = current.RememberConversations;
        _greeting.IsToggled = current.GreetOnStartup; _hourly.IsToggled = current.AnnounceHourly; _autoStart.IsToggled = current.StartWithWindows;
        _clapWake.IsToggled = current.ClapWakeEnabled; _clapSensitivity.Value = current.ClapWakeSensitivity;
        _volume.Value = current.Voice.Volume; _silence.Value = current.GenerativeAi.SilenceDurationMs;
        for (int index = 1; index < _inputIds.Length; index++) { string name = AudioDeviceNames.Input(_inputIds[index], _ui.Get("ui.maui.unavailable")); if (_input.Items[index] != name) { _input.Items[index] = name; } }
        for (int index = 1; index < _outputIds.Length; index++) { string name = AudioDeviceNames.Output(_outputIds[index], _ui.Get("ui.maui.unavailable")); if (_output.Items[index] != name) { _output.Items[index] = name; } }
        _input.SelectedIndex = Math.Max(0, Array.IndexOf(_inputIds, current.InputDevice)); _output.SelectedIndex = Math.Max(0, Array.IndexOf(_outputIds, current.OutputDevice));
        _appSearch.Text = ""; SelectTab(0); RefreshKeyState();
    }
    /// <summary>Selects a single tab while preserving unsaved values.</summary>
    public void SelectTab(int index)
    {
        if (index < 0 || index >= _pages.Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
        _activeTab = index;
        _active.Content = _pages[index];
        if (index == 3) { RefreshApplications(); }
        for (int item = 0; item < _tabs.Length; item++)
        {
            Theme.SetSelected(_tabs[item], item == index, _tabs[item].Text + (item == index ? " · " + _ui.Get("ui.navigation.selected") : ""));
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
        Content = Theme.Icon(icon, true, 22), WidthRequest = 44, HeightRequest = 44,
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
    /// <summary>Builds connection controls without exposing the protected saved credential.</summary>
    private View ConnectionPanel()
    {
        var provider = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 16 };
        provider.Add(Badge("brain")); provider.Add(Theme.Stack(Theme.Label("Google AI Studio", 22), Theme.Label(_ui.Get("ui.maui.googleIntro"), 13, Theme.Muted)), 1);
        _providerState.VerticalOptions = LayoutOptions.Start; provider.Add(_providerState, 2);
        var keyField = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8 };
        keyField.Add(_key); keyField.Add(_revealKey, 1);
        keyField.Add(ActionIcon("copy", "ui.settings.copyKey", async () =>
        {
            string credential = string.IsNullOrWhiteSpace(_key.Text) ? _keys.Read() : _key.Text ?? "";
            if (!string.IsNullOrEmpty(credential)) { await Clipboard.Default.SetTextAsync(credential).ConfigureAwait(true); _result.Text = _ui.Get("ui.settings.copied"); }
        }), 2);
        var protectedState = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 10 };
        protectedState.Add(Theme.Icon("shield_check")); protectedState.Add(_keyState, 1);
        var actions = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 12 };
        actions.Add(ActionButton("ui.maui.importMark", "upload", ImportMarkAsync));
        var remove = ActionButton("ui.maui.removeKey", "trash", RemoveKeyAsync); remove.BackgroundColor = Theme.AssistantSurface; actions.Add(remove, 1);
        _localConnection = Theme.Stack(_localModels, Row("ui.models.allowCloud", "globe", _cloudFallback), Notice("ui.models.fallbackHelp"));
        _cloudConnection = Theme.Stack(provider, Theme.Caption(_ui.Get("ui.maui.key")), keyField, protectedState, actions,
            Row("ui.ai.model", "box", _model), Notice("ui.maui.cloudPrivacy"),
            Theme.Label("GPT · OpenAI", 22), _openAiKey, Row("ui.ai.model", "box", _openAiModel),
            ActionButton("ui.ai.removeOpenAiKey", "trash", async () =>
            {
                await _router.ResetSessionAsync(() => { _openAiKeys.Save(""); return Task.CompletedTask; }).ConfigureAwait(true);
                _openAiKey.Text = ""; _openAiKey.Placeholder = "OpenAI API key";
            }), Notice("ui.ai.openAiPrivacy"));
        RefreshProviderPanels();
        return Panel(Row("ui.ai.provider", "brain", _chatProvider), Notice("ui.ai.providerHelp"), _localConnection, _cloudConnection);
    }
    /// <summary>Shows the selected provider's configuration while preserving independent saved credentials and model choices.</summary>
    private void RefreshProviderPanels()
    {
        if (_localConnection is null || _cloudConnection is null) { return; }
        _localConnection.IsVisible = _chatProvider.SelectedIndex >= 2;
        _cloudConnection.IsVisible = _chatProvider.SelectedIndex < 2 || _chatProvider.SelectedIndex == 3 && _cloudFallback.IsToggled;
        if (_cloudConnection.IsVisible && _model.IsLoaded) { _ = RefreshCloudModelsAsync(); }
    }
    /// <summary>Refreshes only explicitly visible cloud configuration controls without blocking native layout.</summary>
    private Task RefreshCloudModelsAsync() => Task.WhenAll(_model.RefreshAsync(), _openAiModel.RefreshAsync());
    /// <summary>Builds voice and local wake controls with their actual units and privacy states.</summary>
    private View VoicePanel() => Panel(
        Row("ui.maui.liveVoice", "audio_lines", _voice),
        Hint("ui.maui.brazilianAccent"),
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
        _body.WidthRequest = Math.Min(980, Math.Max(320, Width - 2 * Theme.PageInset));
        bool brandVisible = Width >= 1360;
        if (_brand.IsVisible != brandVisible || _workspaceHeader.ColumnDefinitions[0].Width.Value != (brandVisible ? 155 : 0))
        {
            _brand.IsVisible = brandVisible;
            _workspaceHeader.ColumnDefinitions[0].Width = new GridLength(brandVisible ? 155 : 0);
            _workspaceHeader.ColumnSpacing = brandVisible ? 12 : 0;
        }
        bool compact = Width < 900;
        if (_compact == compact) { return; }
        _compact = compact;
        _tabLayout.ColumnDefinitions = compact ? [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)] : [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)];
        _tabLayout.RowDefinitions = compact ? [new(GridLength.Auto), new(GridLength.Auto)] : [new(GridLength.Auto)];
        for (int index = 0; index < _tabs.Length; index++) { Grid.SetColumn(_tabs[index], compact ? index % 3 : index); Grid.SetRow(_tabs[index], compact ? index / 3 : 0); }
        foreach (var (row, control, toggle) in _responsiveRows)
        {
            row.ColumnDefinitions = compact || toggle ? [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] : [new(GridLength.Auto), new(new GridLength(250)), new(GridLength.Star)];
            bool stack = compact && !toggle || control is Editor;
            row.RowDefinitions = stack ? [new(GridLength.Auto), new(GridLength.Auto)] : [new(GridLength.Auto)];
            Grid.SetRow(control, stack ? 1 : 0); Grid.SetColumn(control, stack ? 1 : 2); Grid.SetColumnSpan(control, stack ? 2 : 1);
        }
    }
    /// <summary>Displays speech-end delay in milliseconds.</summary>
    private void UpdateSilence() => _silenceValue.Text = $"{_silence.Value:F0} ms";
    /// <summary>Shows a fixed mask for a stored credential without placing the secret or a fake key in editable text.</summary>
    private void RefreshKeyState()
    {
        bool saved = !string.IsNullOrWhiteSpace(_keys.Read());
        _key.Placeholder = saved ? "••••••••••••••••" : _ui.Get("ui.maui.keyPlaceholder");
        _key.PlaceholderColor = saved ? Theme.Text : Theme.Muted;
        if (string.IsNullOrEmpty(_key.Text))
        {
            _key.IsPassword = true; _revealKey.Source = Theme.IconSource("eye");
            Theme.DescribeAction(_revealKey, _ui.Get("ui.settings.revealKey"));
        }
        _keyState.Text = _ui.Get(saved ? "ui.maui.keySaved" : "ui.maui.noKey");
        _providerState.Text = _ui.Get(saved ? "ui.settings.configured" : "ui.settings.notConfigured");
        _providerState.TextColor = saved ? Theme.Text : Theme.Muted;
    }
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
        if (revision == _searchRevision && _activeTab == 3) { RefreshApplications(); }
    }
    /// <summary>Gets the actual number of settings tabs, including native MCP configuration.</summary>
    public int TabCount => _pages.Length;
    /// <summary>Checks independent protected keys and provider switching using only synthetic credentials in isolated UI diagnostics.</summary>
    internal async Task VerifyProvidersForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated UI diagnostics only."); }
        var original = _settings.Current; string google = _keys.Read(), openai = _openAiKeys.Read();
        try
        {
            _keys.Save("synthetic-google-credential"); _openAiKeys.Save("synthetic-openai-credential");
            ReloadFromSettings(); _chatProvider.SelectedIndex = 1;
            if (!await SaveAsync().ConfigureAwait(true) || _settings.Current.GenerativeAi.Provider != "openai" || !_router.IsAvailable || !_gpt.IsAvailable || _live.IsAvailable)
            { throw new InvalidOperationException("Optional GPT provider selection did not route availability correctly."); }
            if (_openAiKey.Text?.Length > 0 || !_openAiKey.IsPassword || _openAiKey.Placeholder != "••••••••••••••••")
            { throw new InvalidOperationException("Protected GPT credential leaked into editable text."); }
            _chatProvider.SelectedIndex = 0;
            if (!await SaveAsync().ConfigureAwait(true) || !_live.IsAvailable || _gpt.IsAvailable || _keys.Read() != "synthetic-google-credential" || _openAiKeys.Read() != "synthetic-openai-credential")
            { throw new InvalidOperationException("Switching back lost independent credentials or provider state."); }
            _openAiKeys.Save(""); _chatProvider.SelectedIndex = 1;
            if (!await SaveAsync().ConfigureAwait(true) || _router.IsAvailable || _live.IsAvailable)
            { throw new InvalidOperationException("Missing optional GPT key silently selected another provider."); }
        }
        finally
        {
            await _router.ResetSessionAsync(async () => { _keys.Save(google); _openAiKeys.Save(openai); await _settings.UpdateAsync(original).ConfigureAwait(false); }).ConfigureAwait(true);
            ReloadFromSettings();
        }
    }

    /// <summary>Shows and checks actual populated cloud pickers using isolated synthetic candidate keys.</summary>
    internal async Task ShowProviderForSmokeAsync(string provider)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated UI diagnostics only."); }
        _key.Text = "synthetic-google-credential"; _openAiKey.Text = "synthetic-openai-credential";
        _chatProvider.SelectedIndex = provider switch { "gemini" => 0, "local" => 2, _ => 1 };
        if (provider == "local") { await _localModels.VerifyHardwareForSmokeAsync().ConfigureAwait(true); return; }
        await (provider == "gemini" ? _model : _openAiModel).VerifyForSmokeAsync().ConfigureAwait(true);
    }
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
        if (_activeTab == 3)
        {
            if (_appList.Height < 80 || _appList.Width < 160) { throw new InvalidOperationException("The application inventory has no usable viewport."); }
            if (_appList.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list && list.Items.Count > 0 && list.ContainerFromIndex(0) is null)
            { throw new InvalidOperationException("The application inventory did not realize its first visible row."); }
        }
        if (!_key.IsPassword && string.IsNullOrEmpty(_key.Text)) { throw new InvalidOperationException("The empty credential entry was left unmasked."); }
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
    /// <summary>Imports a selected key and native voice without editing Mark-LV.</summary>
    private async Task ImportMarkAsync()
    {
        FileResult? file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = _ui.Get("ui.maui.importMark") }).ConfigureAwait(true);
        if (file is null) { return; }
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullPath).ConfigureAwait(true));
        if (!document.RootElement.TryGetProperty("gemini_api_key", out JsonElement credential) || string.IsNullOrWhiteSpace(credential.GetString())) { _result.Text = _ui.Get("ui.maui.noImportKey"); return; }
        _key.Text = credential.GetString();
        if (document.RootElement.TryGetProperty("voice_name", out JsonElement voice) && GeminiLiveOptions.Voices.Contains(voice.GetString(), StringComparer.Ordinal)) { _voice.SelectedItem = voice.GetString(); }
        _result.Text = _ui.Get("ui.maui.imported");
    }
    /// <summary>Removes the credential and closes the previous connection.</summary>
    private async Task RemoveKeyAsync() { await _router.ResetSessionAsync(() => { _keys.Save(""); return Task.CompletedTask; }).ConfigureAwait(true); _key.Text = ""; RefreshKeyState(); }
    /// <summary>Saves all tabs through the existing settings service.</summary>
    private async Task<bool> SaveAsync()
    {
        string? candidate = string.IsNullOrWhiteSpace(_key.Text) ? null : _key.Text.Trim();
        string? gptCandidate = string.IsNullOrWhiteSpace(_openAiKey.Text) ? null : _openAiKey.Text.Trim();
        string selectedProvider = _chatProvider.SelectedIndex switch { 0 => "gemini", 1 => "openai", 2 => "local", _ => "auto" };
        bool configured = selectedProvider is "local" or "auto" || (selectedProvider == "openai" ? gptCandidate is not null || !string.IsNullOrWhiteSpace(_openAiKeys.Read()) : candidate is not null || !string.IsNullOrWhiteSpace(_keys.Read()));
        string gptModel = (_settings.Current.GenerativeAi with { OpenAiModel = _openAiModel.Text ?? "" }).Sanitized().OpenAiModel;
        if (gptCandidate is not null)
        {
            _result.Text = _ui.Get("ui.maui.validatingKey");
            using var validation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await _gpt.ValidateCredentialAsync(gptCandidate, gptModel, validation.Token).ConfigureAwait(true); }
            catch (Exception error) when (error is InvalidOperationException or System.Net.Http.HttpRequestException or IOException or OperationCanceledException)
            { _result.Text = _ui.Get("ui.maui.invalidKey"); return false; }
        }
        if (candidate is not null)
        {
            _result.Text = _ui.Get("ui.maui.validatingKey");
            using var validation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await _live.ValidateCredentialAsync(candidate, _settings.Current.GenerativeAi with
                    { Model = _model.Text ?? "", LiveVoice = _voice.SelectedItem as string ?? "Aoede" },
                    _culture.SelectedItem as string ?? "pt-BR", validation.Token).ConfigureAwait(true);
            }
            catch (Exception error) when (error is InvalidOperationException or System.Net.WebSockets.WebSocketException or IOException or OperationCanceledException)
            { _result.Text = _ui.Get("ui.maui.invalidKey"); return false; }
        }
        LauraSettings updated = _settings.Current with
        {
            Culture = _culture.SelectedItem as string ?? "pt-BR", UiCulture = _uiCulture.SelectedItem as string ?? "pt-BR",
            UserNickname = _nickname.Text ?? "", MicrophoneMuted = _muted.IsToggled, RememberConversations = _remember.IsToggled,
            ClapWakeEnabled = _clapWake.IsToggled, ClapWakeSensitivity = (int)_clapSensitivity.Value,
            InputDevice = _inputIds[Math.Max(0, _input.SelectedIndex)], OutputDevice = _outputIds[Math.Max(0, _output.SelectedIndex)],
            GreetOnStartup = _greeting.IsToggled, AnnounceHourly = _hourly.IsToggled, StartWithWindows = _autoStart.IsToggled,
            Permissions = _permissions.Options, Screenshots = _screenshots.Options,
            Voice = _settings.Current.Voice with { VoiceName = null, Volume = (int)_volume.Value },
            GenerativeAi = _settings.Current.GenerativeAi with { Enabled = configured, Provider = selectedProvider, AllowCloudFallback = _cloudFallback.IsToggled, LocalModel = _localModels.ModelId, Model = _model.Text ?? "", OpenAiModel = gptModel, Persona = (_persona.Text ?? "").ReplaceLineEndings(" "), LiveVoice = _voice.SelectedItem as string ?? "Aoede", ContinuousListening = true, SilenceDurationMs = (int)_silence.Value }
        };
        await _router.ResetSessionAsync(async () =>
        {
            if (candidate is not null) { _keys.Save(candidate); }
            if (gptCandidate is not null) { _openAiKeys.Save(gptCandidate); }
            await _settings.UpdateAsync(updated).ConfigureAwait(false);
        }).ConfigureAwait(true);
        if (candidate is not null && _key.Text?.Trim() == candidate) { _key.Text = ""; }
        if (gptCandidate is not null && _openAiKey.Text?.Trim() == gptCandidate) { _openAiKey.Text = ""; }
        if (candidate is not null) { await _model.RefreshAsync(true).ConfigureAwait(true); }
        if (gptCandidate is not null) { await _openAiModel.RefreshAsync(true).ConfigureAwait(true); }
        _openAiKey.Placeholder = string.IsNullOrWhiteSpace(_openAiKeys.Read()) ? "OpenAI API key" : "••••••••••••••••";
        _ui.SetCulture(updated.ResolveUiCulture());
        if (_startup.IsEnabled() != updated.StartWithWindows) { _startup.SetEnabled(updated.StartWithWindows); }
        RefreshKeyState(); _result.Text = _ui.Get("ui.maui.saved"); return true;
    }
    /// <summary>Previews Gemini after applying the language and voice settings.</summary>
    private async Task PreviewAsync()
    {
        if (!await SaveAsync().ConfigureAwait(true)) { return; }
        if (!_live.IsAvailable) { _result.Text = _ui.Get("ui.maui.noKey"); return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        string preview = _conversation.Get("ui.voice.previewText");
        await _speech.SpeakAsync(new SpeechRequest(preview, _settings.Current.Voice, _settings.Current.Culture), timeout.Token).ConfigureAwait(true);
    }
    internal async Task SaveSettingsForPermissionSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--permissions-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated permission diagnostics only."); }
        if (!await SaveAsync().ConfigureAwait(true)) { throw new InvalidOperationException("Security settings could not be saved."); }
    }

    /// <summary>Exercises masked save and invalid-key preservation only in explicitly requested isolated verification.</summary>
    public async Task VerifyCredentialInputForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Credential verification requires isolated UI smoke mode."); }
        string original = _keys.Read();
        if (string.IsNullOrEmpty(original)) { throw new InvalidOperationException("Import an explicit test credential before running key verification."); }
        _key.Text = original;
        if (!await SaveAsync().ConfigureAwait(true)) { throw new InvalidOperationException("Valid credential save was rejected."); }
        if (!string.IsNullOrEmpty(_key.Text) || _key.Placeholder != "••••••••••••••••") { throw new InvalidOperationException("Stored key is not safely masked."); }
        if (!await SaveAsync().ConfigureAwait(true) || _keys.Read() != original) { throw new InvalidOperationException("Saving other settings overwrote the masked credential."); }
        _key.Text = original;
        Task<bool> pendingSave = SaveAsync();
        _key.Text = "invalid-laura-smoke-key";
        if (!await pendingSave.ConfigureAwait(true) || _keys.Read() != original || _key.Text != "invalid-laura-smoke-key")
        { throw new InvalidOperationException("Editing during validation persisted an unverified credential or lost the new draft."); }
        _key.Text = "invalid-laura-smoke-key";
        if (await SaveAsync().ConfigureAwait(true) || _keys.Read() != original) { throw new InvalidOperationException("An invalid candidate replaced the saved credential."); }
        _key.Text = ""; RefreshKeyState();
    }
}

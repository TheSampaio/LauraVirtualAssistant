using Laura.App.Controls;
using Laura.App.Infrastructure;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace Laura.App.Views;

/// <summary>Responsive three-column MAUI HUD with a vector reactor and native chat and microphone controls.</summary>
public sealed partial class MainPage : ContentPage, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAssistantEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IUiLocalizer _ui;
    private readonly GeminiLiveClient _live;
    private readonly AssistantCoreDrawable _core = new();
    private readonly GraphicsView _reactor;
    private readonly Grid _body = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly ObservableCollection<ConversationMessage> _messages = [];
    private readonly CollectionView _chat;
    private readonly Editor _draft = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44, MaximumHeightRequest = 180, TextColor = Theme.Text, FontSize = 14, PlaceholderColor = Theme.Muted };
    private readonly Label _status = Theme.Label("Ready", 15, Theme.Accent);
    private readonly Label _provider = Theme.Label("", 12, Theme.Muted);
    private readonly Label _clock = Theme.Label("", 22, Theme.Accent);
    private readonly Label _metrics = Theme.Label("", 11, Theme.Muted);
    private readonly ResourceMeter _cpuMeter = new("cpu"), _memoryMeter = new("memory_stick"), _networkMeter = new("ethernet");
    private readonly ResourceMeter _gpuMeter = new("gpu"), _vramMeter = new("memory_stick");
    private readonly VerticalStackLayout _diskRows = new() { Spacing = 0 };
    private readonly Dictionary<uint, ResourceMeter> _diskMeters = [];
    private readonly Label _diskUnavailable = Theme.Label("", 12, Theme.Muted);
    private readonly Label _shortcutHint = Theme.Label("", 11, Theme.Muted);
    private readonly WindowsResourceMonitor _resources;
    private readonly WindowsClapListener _clapListener;
    private readonly Label _micStatus = Theme.Label("", 12, Theme.Muted);
    private readonly ImageButton _listen;
    private readonly Border _monitor;
    private readonly Border _center;
    private readonly Border _activity;
    private IDispatcherTimer? _timer;
    private CancellationTokenSource? _holdLifetime;
    private CancellationTokenSource? _command;
    private bool _narrow;
    private bool _loaded;
    private long _nextMetrics;
    private bool _windowVisible = true;
    private bool _pageVisible;
    private ReactorMode? _smokeMode;
    private readonly Label _monitorTitle;
    private readonly Label _coreTitle;
    private readonly Label _activityTitle;
    private readonly ImageButton _setupButton;
    private readonly Button _approvalsButton;
    private readonly Button _memoryButton;
    private readonly ImageButton _tasksButton;
    private readonly ImageButton _interruptButton;
    private readonly ImageButton _microphoneButton;
    private readonly ImageButton _sendButton;
    private readonly ImageButton _monitorButton;
    private readonly Label _subtitle = Theme.Label("", 10, Theme.Muted);
    private readonly Label _deviceTitle = Theme.Caption("");
    private readonly Label _devices = Theme.Label("", 11, Theme.Muted);
    private readonly Label _platform = Theme.Label("", 12);
    private readonly Label _emptyTitle = Theme.Label("", 16);
    private readonly Label _emptyHint = Theme.Label("", 13, Theme.Muted);
    private readonly View _emptyState;
    private readonly Label _voiceHint = Theme.Label("", 12, Theme.Muted);
    private readonly Image _micIcon = Theme.Icon("mic_off", size: 16);
    private readonly ScrollView _monitorScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Always, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
    private long _lastFrame;
    private SetupPage? _setupPage;
    private MemoryPage? _brainPage;
    private string _chatCulture = "";
    private float? _smokeAngle;
    private bool _showMonitor;
    private bool _samplingMetrics;
    private ReactorMode _visualMode;
    private string _reportedStatus = "Ready";
    private readonly global::Windows.UI.ViewManagement.UISettings _accessibility = new();

    /// <summary>Composes the HUD and subscribes to domain and Live events through the UI dispatcher.</summary>
    public MainPage(IServiceProvider services, IAssistantEngine engine, ISettingsService settings, IUiLocalizer ui, GeminiLiveClient live, WindowsResourceMonitor resources, WindowsClapListener clapListener)
    {
        using var performance = PerformanceTrace.Measure("hud.construct");
        _services = services; _engine = engine; _settings = settings; _ui = ui; _live = live; _resources = resources; _clapListener = clapListener;
        _conversations = services.GetRequiredService<ConversationActions>();
        _returnLatest = Theme.IconButton("chevron_down", FollowLatestAsync);
        _returnLatest.IsVisible = false; _returnLatestHost.Add(_returnLatest);
        var unreadBadge = Theme.Card(_unreadCount, 3); unreadBadge.BackgroundColor = Theme.Primary; unreadBadge.StrokeThickness = 0;
        unreadBadge.HorizontalOptions = LayoutOptions.End; unreadBadge.VerticalOptions = LayoutOptions.Start; unreadBadge.InputTransparent = true; unreadBadge.IsVisible = false; _returnLatestHost.Add(unreadBadge);
        _conversationScroll = new((visible, unread) => { _returnLatest.IsVisible = visible; _returnLatestHost.IsVisible = visible; unreadBadge.IsVisible = unread > 0; _unreadCount.Text = unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Theme.DescribeAction(_returnLatest, _ui.Get("ui.chat.latest") + (unread > 0 ? " · " + _unreadCount.Text : "")); });
        BackgroundColor = Theme.Background;
        NavigationPage.SetHasNavigationBar(this, false);
        _reactor = new GraphicsView { Drawable = _core, MinimumHeightRequest = 180, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(_reactor, "Laura");
        _chat = new CollectionView { ItemsSource = _messages, SelectionMode = SelectionMode.None, ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset, VerticalOptions = LayoutOptions.Fill, BackgroundColor = Theme.Surface };
        _chat.ItemTemplate = new DataTemplate(() =>
        {
            var cell = new ContentView { Padding = new Thickness(0, 4, 4, 8) };
            cell.BindingContextChanged += (_, _) => { if (cell.BindingContext is ConversationMessage message) { cell.Content = new ConversationBubble(message, Author(message), _ui); } };
            return cell;
        });
        _chat.Loaded += (_, _) => AttachConversationScroll(); _chat.HandlerChanged += (_, _) => AttachConversationScroll(); _chat.SizeChanged += (_, _) => RequestConversationScroll();
        _listen = Theme.IconButton("audio_lines", ToggleListeningAsync);
        _monitorTitle = Theme.Caption(""); _coreTitle = Theme.Caption(""); _activityTitle = Theme.Caption("");
        _setupButton = Theme.IconButton("settings_2", OpenSetupAsync);
        _approvalsButton = Theme.ActionButton("", "shield_check", () => { _services.GetRequiredService<Laura.App.Platforms.Windows.WindowsApprovalCenter>().Show(); return Task.CompletedTask; });
        _services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().Changed += OnApprovalsChanged;
        UpdateApprovals();
        _memoryButton = Theme.ActionButton("", "brain", OpenBrainAsync);
        _tasksButton = Theme.IconButton("activity", () => NavigationMotion.PushAsync(Navigation, _services.GetRequiredService<TasksPage>()));
        _interruptButton = Theme.IconButton("square", InterruptAsync);
        _microphoneButton = Theme.IconButton("mic_off", ToggleMicrophoneAsync);
        _sendButton = Theme.IconButton("send", SendAsync, true, CanSend);
        _monitorButton = Theme.IconButton("activity", () => { _showMonitor = !_showMonitor; SetLayout(_narrow); return Task.CompletedTask; });
        var screenshot = Theme.IconButton("scan", async () => { await _services.GetRequiredService<ScreenshotCoordinator>().CaptureAsync(Laura.Core.Screenshots.ScreenshotMode.Region, true).ConfigureAwait(true); });
        Theme.DescribeAction(screenshot, _ui.Get("ui.screenshot.capture.Region"));
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Margin = new Thickness(0, 0, 0, 20) };
        var brand = Theme.Stack(new Label { Text = "L A U R A", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Theme.Accent }, _subtitle); brand.Spacing = 4;
        header.Add(brand);
        _clock.FontSize = 18; _clock.Margin = new Thickness(0, 0, 8, 0); _clock.VerticalOptions = LayoutOptions.Center;
        var actions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        actions.Add(_clock); actions.Add(_monitorButton); actions.Add(screenshot); actions.Add(_approvalsButton); actions.Add(_setupButton); header.Add(actions, 1);
        _monitor = BuildMonitor();
        var centerGrid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 12 };
        centerGrid.Add(SectionHeader("audio_lines", _coreTitle)); centerGrid.Add(_reactor, 0, 1);
        var footer = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        var state = Theme.Stack(_status, _provider); state.Spacing = 4; state.VerticalOptions = LayoutOptions.Center;
        footer.Add(state); footer.Add(new HorizontalStackLayout { Spacing = 8, Children = { _tasksButton, _memoryButton } }, 1); centerGrid.Add(footer, 0, 2); _center = Theme.Card(centerGrid, 20);
        _center.BackgroundColor = Theme.Background;
        var chatGrid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 16 };
        var chatHeader = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        chatHeader.Add(SectionHeader("activity", _activityTitle));
        _chatActionsButton = Theme.IconButton("ellipsis", ChatActionsAsync); chatHeader.Add(_chatActionsButton, 1);
        chatGrid.Add(chatHeader);
        var conversation = new Grid(); conversation.Add(_chat);
        var empty = Theme.Stack(Theme.Icon("audio_lines", true, 32), _emptyTitle, _emptyHint); empty.Spacing = 12;
        empty.HorizontalOptions = LayoutOptions.Center; empty.VerticalOptions = LayoutOptions.Center; empty.Padding = 16;
        _emptyTitle.HorizontalTextAlignment = TextAlignment.Center; _emptyHint.HorizontalTextAlignment = TextAlignment.Center;
        _emptyState = empty; conversation.Add(empty); conversation.Add(_returnLatestHost); chatGrid.Add(conversation, 0, 1);
        var compose = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        _draft.BackgroundColor = Colors.Transparent;
        compose.Add(_draft); compose.Add(_sendButton, 1);
        var input = Theme.Card(compose, 4); input.BackgroundColor = Theme.Field; input.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = Theme.ControlRadius };
        _draft.HandlerChanged += (_, _) =>
        { ConfigureComposer(); };
        _draft.Focused += (_, _) => { input.Stroke = Theme.Accent; input.StrokeThickness = 1.5; };
        _draft.Unfocused += (_, _) => { input.Stroke = Theme.Line; input.StrokeThickness = 1; };
        _draft.TextChanged += (_, _) => UpdateSendAction();
        var voiceActions = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 8 };
        voiceActions.Add(_microphoneButton); voiceActions.Add(_listen, 1); voiceActions.Add(_interruptButton, 2);
        _voiceHint.VerticalOptions = LayoutOptions.Center; _voiceHint.HorizontalTextAlignment = TextAlignment.End; voiceActions.Add(_voiceHint, 3);
        _chatNotice.IsVisible = false;
        var controls = Theme.Stack(voiceActions, input, _shortcutHint, _chatNotice); controls.Spacing = 12;
        chatGrid.Add(controls, 0, 2); _activity = Theme.Card(chatGrid, 20);
        var root = new Grid { Padding = 24, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        root.Add(header); root.Add(_body, 0, 1); Content = root;
        SetLayout(false);
        SizeChanged += (_, _) => { if (Width <= 0) { return; } bool narrow = Width < 1220; if (narrow != _narrow) { SetLayout(narrow); } };
        engine.ConversationMessageReceived += ReceiveMessage;
        engine.StateChanged += (_, state) => MainThread.BeginInvokeOnMainThread(() => SetState(state.ToString()));
        live.MessageReceived += ReceiveMessage;
        live.StreamUpdated += ReceiveStream;
        services.GetRequiredService<OpenAiConversationEngine>().StreamUpdated += ReceiveStream;
        services.GetRequiredService<OpenAiConversationEngine>().Interrupted += StreamInterrupted;
        services.GetRequiredService<Laura.Platform.Windows.Conversation.LlamaLocalInferenceEngine>().StreamUpdated += ReceiveStream;
        services.GetRequiredService<Laura.Platform.Windows.Conversation.LlamaLocalInferenceEngine>().Interrupted += StreamInterrupted;
        services.GetRequiredService<LocalVoiceSession>().Changed += LocalCaptureChanged;
        live.Interrupted += StreamInterrupted;
        live.StatusChanged += (_, state) => MainThread.BeginInvokeOnMainThread(() => SetState(state));
        settings.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Reload);
        clapListener.CaptureChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshFeedback);
    }
    /// <summary>Pairs a restrained section caption with the shared icon set.</summary>
    private static View SectionHeader(string icon, Label title)
    { var header = new HorizontalStackLayout { Spacing = 12, MinimumHeightRequest = 28 }; header.Add(Theme.Icon(icon, true, 18)); header.Add(title); return header; }
    /// <summary>Keeps real metrics and device information scrollable at short window heights.</summary>
    private Border BuildMonitor()
    {
        var content = Theme.Stack(_cpuMeter, _memoryMeter, _gpuMeter, _vramMeter, _diskRows, _networkMeter); content.Spacing = 0;
        content.Padding = new Thickness(0, 0, 16, 8);
        content.Add(new BoxView { Color = Theme.Line, HeightRequest = 1, Margin = new Thickness(0, 12) });
        content.Add(_deviceTitle);
        var microphone = new Grid { ColumnDefinitions = [new(new GridLength(16)), new(GridLength.Star)], ColumnSpacing = 8, Margin = new Thickness(0, 12, 0, 8) };
        microphone.Add(_micIcon); microphone.Add(_micStatus, 1); content.Add(microphone);
        content.Add(_devices);
        content.Add(new BoxView { Color = Theme.Line, HeightRequest = 1, Margin = new Thickness(0, 16) });
        content.Add(SectionHeader("monitor", _platform)); content.Add(_metrics);
        var panel = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], RowSpacing = 12, Padding = new Thickness(20, 20, 4, 20) };
        _monitorScroll.Content = content;
        var header = SectionHeader("activity", _monitorTitle); header.Margin = new Thickness(0, 0, 16, 0);
        panel.Add(header); panel.Add(_monitorScroll, 0, 1);
        return Theme.Card(panel, 0);
    }
    /// <summary>Refreshes settings-driven labels and restores bounded history once.</summary>
    public void Reload()
    {
        using var performance = PerformanceTrace.Measure("hud.reload");
        if (_chatCulture != _ui.Culture.Name) { _chatCulture = _ui.Culture.Name; _chat.ItemsSource = null; _chat.ItemsSource = _messages; }
        _draft.Placeholder = _ui.Get("ui.hud.compose");
        Theme.DescribeAction(_chatActionsButton, _ui.Get("ui.chat.actions"));
        _subtitle.Text = _ui.Get("ui.hud.subtitle");
        _monitorTitle.Text = _ui.Get("ui.maui.monitor").ToUpperInvariant(); _coreTitle.Text = _ui.Get("ui.maui.core").ToUpperInvariant(); _activityTitle.Text = _ui.Get("ui.maui.activity").ToUpperInvariant();
        Theme.DescribeAction(_setupButton, _ui.Get("ui.maui.setup")); _memoryButton.Text = _ui.Get("ui.tab.memory");
        Theme.DescribeAction(_tasksButton, _ui.Get("ui.tasks.title"));
        Theme.DescribeAction(_memoryButton, _ui.Get("ui.tab.memory"));
        Theme.DescribeAction(_interruptButton, _ui.Get("ui.maui.interrupt") + " · Esc");
        Theme.DescribeAction(_monitorButton, _ui.Get(_showMonitor ? "ui.maui.core" : "ui.maui.monitor")); Theme.DescribeAction(_sendButton, _ui.Get("ui.hud.send"));
        SemanticProperties.SetDescription(_draft, _ui.Get("ui.hud.compose"));
        _deviceTitle.Text = _ui.Get("ui.hud.devices").ToUpperInvariant();
        _platform.Text = $"Windows · {Environment.OSVersion.Version.Build}";
        _devices.Text = DeviceSummary();
        string activeProvider = _services.GetRequiredService<ConversationProviderRouter>().ActiveProvider;
        _emptyTitle.Text = _ui.Get("ui.hud.ready"); _emptyHint.Text = _ui.Get("ui.hud.prompt");
        _shortcutHint.Text = _ui.Get(activeProvider is "local" or "gemini" ? "ui.hud.talkHint" : "ui.ai.textHint");
        ToolTipProperties.SetText(_shortcutHint, activeProvider is "local" or "gemini" ? _ui.Get("ui.maui.shortcutHelp") + "\n" + _ui.Get("ui.hud.hotkey") : _shortcutHint.Text);
        ToolTipProperties.SetText(_networkMeter, _ui.Get("ui.maui.networkHint"));
        _diskUnavailable.Text = _ui.Get("ui.hud.disksUnavailable");
        RefreshFeedback(); UpdateSendAction(); _nextMetrics = 0;
        if (!_loaded && _windowVisible && _pageVisible && _engine.State != AssistantState.Stopped) { _messages.Clear(); foreach (ConversationMessage message in _engine.ConversationHistory) { _messages.Add(message); } _loaded = true; RequestConversationScroll(); }
        _emptyState.IsVisible = _messages.Count == 0;
    }
    /// <summary>Names the configured audio endpoints without inventing device availability.</summary>
    private string DeviceSummary()
    {
        int input = _settings.Current.InputDevice, output = _settings.Current.OutputDevice;
        string microphone = input < 0 ? _ui.Get("ui.maui.systemDefault") : AudioDeviceNames.Input(input, _ui.Get("ui.maui.unavailable"));
        string speaker = output < 0 ? _ui.Get("ui.maui.systemDefault") : AudioDeviceNames.Output(output, _ui.Get("ui.maui.unavailable"));
        return _ui.Get("ui.maui.input") + " · " + microphone + "\n" + _ui.Get("ui.maui.output") + " · " + speaker;
    }
    /// <summary>Synchronizes voice controls and explicit capture feedback with current runtime state.</summary>
    private void RefreshFeedback()
    {
        var localVoice = _services.GetRequiredService<LocalVoiceSession>();
        bool muted = _settings.Current.MicrophoneMuted, listening = _live.IsListening || localVoice.IsRunning;
        string activeProvider = _services.GetRequiredService<ConversationProviderRouter>().ActiveProvider;
        string providerText = activeProvider switch { "openai" => "GPT · " + _settings.Current.GenerativeAi.OpenAiModel,
            "gemini" => "GEMINI LIVE · " + _settings.Current.GenerativeAi.LiveVoice, "local" => "LOCAL · " + _settings.Current.GenerativeAi.LocalModel, _ => _ui.Get("ui.models.noProvider") };
        if (_provider.Text != providerText) { _provider.Text = providerText; }
        bool voiceSupported = activeProvider is "gemini" or "local";
        _listen.IsEnabled = voiceSupported; _microphoneButton.IsEnabled = voiceSupported;
        string micAction = _ui.Get(muted ? "ui.hud.unmute" : "ui.hud.mute");
        _microphoneButton.Source = Theme.IconSource(muted ? "mic_off" : "mic"); _micIcon.Source = _microphoneButton.Source;
        _microphoneButton.BackgroundColor = muted ? Theme.Field : Theme.AssistantSurface;
        Theme.DescribeAction(_microphoneButton, micAction + " · M");
        Theme.DescribeAction(_listen, _ui.Get(listening ? "ui.maui.stopRecording" : "ui.maui.startListening"));
        _listen.BackgroundColor = listening ? Theme.Accent : Theme.Field;
        _micStatus.Text = _ui.Get(muted ? "ui.maui.muted" : listening && _live.IsSpeaking ? "ui.hud.capturePaused" : listening ? "ui.maui.streaming" : _clapListener.IsCapturing ? "ui.hud.clapLocal" : "ui.maui.micReady");
        _voiceHint.Text = _ui.Get(muted ? "ui.hud.micMuted" : listening ? "ui.maui.listening" : _clapListener.IsCapturing ? "ui.hud.clapArmed" : "ui.hud.micReady");
        _voiceHint.TextColor = listening ? Theme.Accent : Theme.Muted;
        if (!voiceSupported) { _micStatus.Text = _ui.Get(activeProvider == "openai" ? "ui.ai.gptTextOnly" : "ui.models.noProvider"); _voiceHint.Text = _micStatus.Text; }
        string state = _visualMode switch { ReactorMode.Speaking => "Speaking", ReactorMode.Thinking => "Thinking", ReactorMode.Listening => "Listening", ReactorMode.Working => "Working", _ => _reportedStatus };
        _status.Text = state switch { "Ready" => _ui.Get("ui.state.ready"), "Speaking" => _ui.Get("ui.state.speaking"), "Thinking" => _ui.Get("ui.state.thinking"), "Connecting" => _ui.Get("ui.state.connecting"), "Working" => _ui.Get("ui.state.working"), "Listening" => _ui.Get("ui.maui.listening"), "Stopped" => _ui.Get("ui.hud.stopped"), _ => state };
        _status.TextColor = _visualMode == ReactorMode.Speaking ? Theme.Speaking : Theme.Accent;
        SemanticProperties.SetDescription(_reactor, "Laura · " + _status.Text);
    }
    /// <summary>Enables sending only when a draft exists and the previous command has completed.</summary>
    private void UpdateSendAction() => _sendButton.IsEnabled = CanSend();
    /// <summary>Checks the same submission condition for typing and action completion.</summary>
    private bool CanSend() => !_clearing && !_conversations.IsResetting && _command is null && !string.IsNullOrWhiteSpace(_draft.Text);
    /// <summary>Opens the native settings workspace.</summary>
    public Task OpenSetupAsync()
    {
        using var performance = PerformanceTrace.Measure("settings.prepare");
        if (Navigation.NavigationStack.Count > 0 && Navigation.NavigationStack[^1] is SetupPage) { return Task.CompletedTask; }
        if (_setupPage is null || !_setupPage.CanReuse) { _setupPage = _services.GetRequiredService<SetupPage>(); }
        else { _setupPage.ReloadFromSettings(); }
        return NavigationMotion.PushAsync(Navigation, _setupPage);
    }
    /// <summary>Opens the Brain through the same native action used by diagnostics.</summary>
    public Task OpenBrainAsync()
    {
        using var performance = PerformanceTrace.Measure("brain.prepare");
        if (Navigation.NavigationStack[^1] is MemoryPage) { return Task.CompletedTask; }
        if (_brainPage is null || !_brainPage.CanReuse) { _brainPage = _services.GetRequiredService<MemoryPage>(); }
        return NavigationMotion.PushAsync(Navigation, _brainPage);
    }
    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing(); _pageVisible = true; _lastFrame = Stopwatch.GetTimestamp(); Reload(); _reactor.Invalidate();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33); _timer.Tick -= Tick; _timer.Tick += Tick;
        if (_windowVisible) { _timer.Start(); }
    }
    /// <inheritdoc />
    protected override void OnDisappearing() { _pageVisible = false; _timer?.Stop(); base.OnDisappearing(); }
    /// <summary>Suspends vector animation while the native window is hidden in the tray.</summary>
    public void SetWindowVisible(bool visible)
    { _windowVisible = visible; _lastFrame = Stopwatch.GetTimestamp(); if (visible && _pageVisible) { Reload(); _reactor.Invalidate(); _timer?.Start(); } else { _timer?.Stop(); } }
    /// <summary>Animates vector rings and refreshes measured system statistics at one-second intervals.</summary>
    private void Tick(object? sender, EventArgs args)
    {
        _core.Mode = _smokeMode ?? (_live.IsSpeaking || _engine.State == AssistantState.Speaking ? ReactorMode.Speaking : _live.IsThinking || _engine.State == AssistantState.Thinking ? ReactorMode.Thinking : _live.IsListening || _services.GetRequiredService<LocalVoiceSession>().IsCapturing ? ReactorMode.Listening : _engine.State == AssistantState.Working ? ReactorMode.Working : ReactorMode.Idle);
        if (_visualMode != _core.Mode) { _visualMode = _core.Mode; RefreshFeedback(); _reactor.Invalidate(); }
        bool animate = _accessibility.AnimationsEnabled;
        long frame = Stopwatch.GetTimestamp();
        double frameDelay = Stopwatch.GetElapsedTime(_lastFrame, frame).TotalMilliseconds;
        if (_windowVisible && _pageVisible && frameDelay > 100) { Laura.Core.Diagnostics.LauraDiagnostics.Record("ui.dispatcher_gap", frameDelay); }
        float seconds = (float)Math.Clamp(Stopwatch.GetElapsedTime(_lastFrame, frame).TotalSeconds, 0, .1); _lastFrame = frame;
        _core.Advance(seconds, animate && _smokeAngle is null);
        float blend = 1 - MathF.Exp(-seconds / .09f);
        _core.Level = animate ? _core.Level + ((_core.Mode == ReactorMode.Listening ? _live.InputLevel : _live.OutputLevel) - _core.Level) * blend : 0;
        if (_smokeAngle is { } angle) { _core.Angle = angle; _core.Level = 0; _reactor.Invalidate(); }
        else if (animate) { _core.Angle = (_core.Angle + (14 + 22 * _core.ActivityAmount) * seconds) % AssistantCoreDrawable.CycleDegrees; _reactor.Invalidate(); }
        if (Environment.TickCount64 < _nextMetrics) { return; }
        _nextMetrics = Environment.TickCount64 + 1000;
        _clock.Text = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        if (!_samplingMetrics) { _ = RefreshMetricsAsync(); }
        if (!animate) { _reactor.Invalidate(); }
    }
    /// <summary>Samples native hardware off the UI thread and publishes one coherent dashboard update.</summary>
    private async Task RefreshMetricsAsync()
    {
        _samplingMetrics = true;
        try
        {
            ResourceSnapshot metrics = await Task.Run(() => _resources.Sample(AppContext.BaseDirectory)).ConfigureAwait(true);
            RenderMetrics(metrics);
        }
        finally { _samplingMetrics = false; }
    }
    /// <summary>Applies measured CPU, RAM, GPU, VRAM, physical disks and wired traffic in display order.</summary>
    private void RenderMetrics(ResourceSnapshot metrics)
    {
        _cpuMeter.Update("CPU", $"{metrics.Cpu:P0}", metrics.Cpu);
        _memoryMeter.Update("RAM", $"{metrics.Memory:P0}", metrics.Memory, $"{metrics.UsedMemory / 1073741824d:F1} / {metrics.TotalMemory / 1073741824d:F1} GB");
        _gpuMeter.Update("GPU", metrics.Gpu is { } usage ? $"{usage:P0}" : "—", metrics.Gpu, metrics.Gpu.HasValue ? "" : _ui.Get("ui.maui.unavailable"));
        ToolTipProperties.SetText(_gpuMeter, metrics.Graphics?.Name ?? _ui.Get("ui.maui.unavailable"));
        GpuSnapshot? graphics = metrics.Graphics;
        double? vram = null;
        string vramDetail = _ui.Get("ui.maui.unavailable");
        if (graphics is { UsedDedicatedMemory: { } usedMemory, TotalDedicatedMemory: > 0 })
        { vram = usedMemory / (double)graphics.TotalDedicatedMemory; vramDetail = $"{usedMemory / 1073741824d:F1} / {graphics.TotalDedicatedMemory / 1073741824d:F1} GB"; }
        _vramMeter.Update("VRAM", vram.HasValue ? $"{vram:P0}" : "—", vram, vramDetail);
        RenderDisks(metrics.Disks);
        EthernetSnapshot ethernet = metrics.Ethernet;
        double? traffic = ethernet.DownloadBytesPerSecond + ethernet.UploadBytesPerSecond;
        double? utilization = ethernet.LinkBitsPerSecond > 0 ? traffic * 8 / ethernet.LinkBitsPerSecond : null;
        _networkMeter.Update("Ethernet", traffic.HasValue ? $"{traffic / 1024:F0} KB/s" : "—", utilization,
            traffic.HasValue ? $"↓ {ethernet.DownloadBytesPerSecond / 1024:F0}   ↑ {ethernet.UploadBytesPerSecond / 1024:F0} KB/s" : _ui.Get(ethernet.Connected ? "ui.maui.unavailable" : "ui.maui.offline"));
        ToolTipProperties.SetText(_networkMeter, ethernet.Name + "\n" + _ui.Get("ui.maui.networkHint"));
        _metrics.Text = $"LAURA {AppInfo.Current.VersionString} · {metrics.AppMemory / 1048576d:F0} MB\n" + _ui.Get("ui.hud.session") + $" · {metrics.Uptime:hh\\:mm\\:ss}";
    }
    /// <summary>Reconciles physical disk rows on hot-plug changes while preserving native disk numbers.</summary>
    private void RenderDisks(IReadOnlyList<PhysicalDiskSnapshot> disks)
    {
        uint[] numbers = [.. disks.Select(disk => disk.Number)];
        if (!_diskMeters.Keys.Order().SequenceEqual(numbers))
        {
            _diskRows.Clear(); _diskMeters.Clear();
            foreach (uint number in numbers) { var meter = new ResourceMeter("hard_drive"); _diskMeters.Add(number, meter); _diskRows.Add(meter); }
            if (numbers.Length == 0) { _diskRows.Add(_diskUnavailable); }
        }
        else if (numbers.Length == 0 && _diskRows.Count == 0) { _diskRows.Add(_diskUnavailable); }
        foreach (PhysicalDiskSnapshot disk in disks)
        {
            string detail = disk.Capacity is { } size ? $"{size / 1073741824d:F0} GB" : _ui.Get("ui.maui.unavailable");
            if (disk.Volumes.Length > 0) { detail += " · " + disk.Volumes; }
            _diskMeters[disk.Number].Update(_ui.Get("ui.maui.disk") + " " + disk.Number, disk.Activity is { } activity ? $"{activity:P0}" : "—", disk.Activity, detail);
            ToolTipProperties.SetText(_diskMeters[disk.Number], _ui.Get("ui.hud.diskActivity"));
        }
    }
    /// <summary>Adapts the HUD to compact windows without overflowing the chat or settings controls.</summary>
    private void SetLayout(bool narrow)
    {
        _narrow = narrow; _body.Children.Clear(); _body.ColumnDefinitions.Clear(); _body.RowDefinitions.Clear();
        _monitorButton.IsVisible = narrow;
        _monitorButton.Source = Theme.IconSource(_showMonitor ? "audio_lines" : "activity");
        Theme.DescribeAction(_monitorButton, _ui.Get(_showMonitor ? "ui.maui.core" : "ui.maui.monitor"));
        _monitorButton.BackgroundColor = _showMonitor ? Theme.AssistantSurface : Theme.Field;
        _monitor.IsVisible = !narrow || _showMonitor; _center.IsVisible = !narrow || !_showMonitor;
        if (narrow)
        {
            _body.ColumnDefinitions.Add(new(GridLength.Star)); _body.ColumnDefinitions.Add(new(new GridLength(1.1, GridUnitType.Star)));
            _body.Add(_showMonitor ? _monitor : _center, 0, 0); _body.Add(_activity, 1, 0);
            _memoryButton.ImageSource = null;
        }
        else
        {
            _body.ColumnDefinitions.Add(new(new GridLength(256))); _body.ColumnDefinitions.Add(new(GridLength.Star)); _body.ColumnDefinitions.Add(new(new GridLength(368)));
            _body.Add(_monitor, 0, 0); _body.Add(_center, 1, 0); _body.Add(_activity, 2, 0);
            _memoryButton.ImageSource = Theme.IconSource("brain");
        }
    }
    /// <summary>Adds a bounded chat bubble and scrolls it into view.</summary>
    private void AddMessage(ConversationMessage message)
    {
        if (message.Source == ConversationMessageSource.Assistant) { RemoveProvisional(); }
        if (_messages.Contains(message)) { return; }
        _messages.Add(message); if (_messages.Count > 200) { _messages.RemoveAt(0); }
        _emptyState.IsVisible = false;
        _conversationScroll.MessageArrived();
        RequestConversationScroll();
    }
    /// <summary>Coalesces scrolling by position after layout, so queued work never references a removed bubble.</summary>
    private void RequestConversationScroll()
    {
        if (_pageVisible) { _conversationScroll.Request(); }
    }
    /// <summary>Resolves the message author's localized display name.</summary>
    private string Author(ConversationMessage message) => message.Source == ConversationMessageSource.Assistant ? "LAURA" : _ui.Get("ui.hud.you");
    /// <summary>Updates state feedback and the core's animation speed.</summary>
    private void SetState(string state)
    {
        _reportedStatus = state;
        RefreshFeedback();
    }
    /// <summary>Submits a text command through the existing assistant queue.</summary>
    private async Task SendAsync()
    {
        string text = _draft.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) || _command is not null || _clearing || _conversations.IsResetting) { return; }
        _command = new CancellationTokenSource(); _draft.Text = ""; UpdateSendAction();
        try { await _engine.SubmitCommandAsync(text, _command.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        finally { _command.Dispose(); _command = null; UpdateSendAction(); }
    }
    /// <summary>Toggles automatic turn detection; a natural speech pause triggers a response without another click.</summary>
    private async Task ToggleListeningAsync()
    {
        if (_services.GetRequiredService<ConversationProviderRouter>().ActiveProvider == "local")
        {
            var local = _services.GetRequiredService<LocalVoiceSession>();
            if (local.IsRunning) { local.Stop(); return; }
            await ListenLocallyAsync(true, CancellationToken.None).ConfigureAwait(true); return;
        }
        if (_services.GetRequiredService<ConversationProviderRouter>().ActiveProvider != "gemini") { Notice(_ui.Get("ui.models.localTextOnly")); return; }
        if (_settings.Current.MicrophoneMuted) { await Laura.App.Platforms.Windows.WindowsDialogs.AlertAsync("Laura", _ui.Get("ui.maui.unmuteFirst"), "OK").ConfigureAwait(true); return; }
        if (_live.IsListening) { await _live.StopListeningAsync().ConfigureAwait(true); }
        else
        {
            using var handoff = await _services.GetRequiredService<ClapWakeCoordinator>().SuspendAsync().ConfigureAwait(true);
            await _live.StartListeningAsync().ConfigureAwait(true);
        }
        Reload();
    }
    /// <summary>Stops microphone capture before persisting its muted state.</summary>
    public async Task ToggleMicrophoneAsync()
    {
        using var handoff = await _services.GetRequiredService<ClapWakeCoordinator>().SuspendAsync().ConfigureAwait(true);
        _services.GetRequiredService<LocalVoiceSession>().Stop();
        if (!_settings.Current.MicrophoneMuted) { _holdLifetime?.Cancel(); await _clapListener.StopAsync().ConfigureAwait(true); await _live.StopListeningAsync().ConfigureAwait(true); }
        await _settings.UpdateAsync(_settings.Current with { MicrophoneMuted = !_settings.Current.MicrophoneMuted }).ConfigureAwait(true);
    }
    /// <summary>Cancels microphone input, native generation, local generation and playback.</summary>
    public async Task InterruptAsync()
    {
        _services.GetRequiredService<LocalVoiceSession>().Stop(); _services.GetRequiredService<ILocalInferenceEngine>().Cancel();
        _holdLifetime?.Cancel(); _command?.Cancel(); _live.Interrupt(); _engine.StopSpeaking(); await _live.StopListeningAsync().ConfigureAwait(true); Reload();
    }
    /// <summary>Captures only while the global Ctrl+Space chord is held, including connection cancellation on release.</summary>
    public async Task SetPushToTalkAsync(bool held)
    {
        if (held && _services.GetRequiredService<ConversationProviderRouter>().ActiveProvider == "local")
        {
            _holdLifetime?.Cancel(); _holdLifetime?.Dispose(); _holdLifetime = new CancellationTokenSource();
            await ListenLocallyAsync(false, _holdLifetime.Token).ConfigureAwait(true); return;
        }
        if (held && _services.GetRequiredService<ConversationProviderRouter>().ActiveProvider != "gemini") { Notice(_ui.Get("ui.models.localTextOnly")); return; }
        if (!held)
        { var lifetime = _holdLifetime; _holdLifetime = null; lifetime?.Cancel(); await _live.StopListeningAsync().ConfigureAwait(true); lifetime?.Dispose(); Reload(); return; }
        if (_settings.Current.MicrophoneMuted) { _micStatus.Text = _ui.Get("ui.maui.unmuteFirst"); return; }
        _holdLifetime?.Cancel(); _holdLifetime?.Dispose(); var capture = new CancellationTokenSource(); _holdLifetime = capture;
        CancellationToken token = capture.Token;
        try
        {
            using var handoff = await _services.GetRequiredService<ClapWakeCoordinator>().SuspendAsync().ConfigureAwait(true);
            _live.Interrupt(); await _live.StopListeningAsync().ConfigureAwait(true); token.ThrowIfCancellationRequested(); await _live.StartListeningAsync(pushToTalk: true, cancellationToken: token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _micStatus.Text = _ui.Get("ui.maui.liveFailed"); }
        Reload();
    }
    /// <summary>Starts only an explicit local microphone workflow and reports actual missing-model/device failures.</summary>
    private async Task ListenLocallyAsync(bool continuous, CancellationToken release)
    {
        if (_settings.Current.MicrophoneMuted || Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { return; }
        var local = _services.GetRequiredService<LocalVoiceSession>(); local.Stop();
        _command?.Cancel(); _services.GetRequiredService<ILocalInferenceEngine>().Cancel(); _engine.StopSpeaking();
        try
        {
            using var handoff = await _services.GetRequiredService<ClapWakeCoordinator>().SuspendAsync().ConfigureAwait(true);
            await local.StartAsync(continuous, release).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or NAudio.MmException)
        { Notice(_ui.Get("ui.models.localVoiceFailed")); }
        Reload();
    }
    /// <summary>Marshals local capture notifications to the native UI dispatcher.</summary>
    private void LocalCaptureChanged(object? sender, EventArgs args) => Dispatcher.Dispatch(RefreshFeedback);
    /// <summary>Previews colors for the isolated rendering harness without recording or playing sound.</summary>
    public void PreviewReactorForSmoke(ReactorMode? mode)
    { if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Visual preview is restricted to isolated smoke verification."); } _smokeMode = mode; _core.Level = .5f; }
    /// <summary>Renders localized sample messages only in the isolated visual harness.</summary>
    internal void PreviewConversationForSmoke()
    {
        EnsureSmoke(); _messages.Clear();
        AddMessage(new(ConversationMessageSource.UserTyped, _ui.Get("ui.hud.prompt"), DateTimeOffset.Now));
        AddMessage(new(ConversationMessageSource.Assistant, _ui.Get("ui.maui.listeningHelp"), DateTimeOffset.Now));
        AddMessage(new(ConversationMessageSource.UserTyped, _ui.Get("ui.hud.compose"), DateTimeOffset.Now));
        AddMessage(new(ConversationMessageSource.Assistant, _ui.Get("ui.maui.memoryPrivacy"), DateTimeOffset.Now));
    }
    /// <summary>Exercises the compact monitor switch without recording audio.</summary>
    internal void PreviewMonitorForSmoke(bool visible)
    { EnsureSmoke(); _showMonitor = visible; SetLayout(_narrow); }
    /// <summary>Checks actual monitor scrolling and the reserved scrollbar gutter.</summary>
    internal async Task VerifyMonitorScrollForSmokeAsync(bool bottom)
    {
        EnsureSmoke();
        if (_monitorScroll.Content is not View content) { throw new InvalidOperationException("Monitor content is unavailable."); }
        if (_cpuMeter.Width > _monitorScroll.Width - 15) { throw new InvalidOperationException("Monitor metrics overlap the native scrollbar gutter."); }
        if (_monitor.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeMonitor && _monitorScroll.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeScroll)
        {
            var origin = nativeScroll.TransformToVisual(nativeMonitor).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            double edgeGap = nativeMonitor.ActualWidth - origin.X - nativeScroll.ActualWidth;
            if (edgeGap > 10 || edgeGap < 0) { throw new InvalidOperationException("Monitor scrollbar is not near the panel edge."); }
        }
        await _monitorScroll.ScrollToAsync(0, bottom ? Math.Max(0, content.Height - _monitorScroll.Height) : 0, false).ConfigureAwait(true);
        await Task.Delay(100).ConfigureAwait(true);
        double expected = bottom ? Math.Max(0, content.Height - _monitorScroll.Height) : 0;
        if (Math.Abs(_monitorScroll.ScrollY - expected) > 2) { throw new InvalidOperationException("Monitor cannot reach its requested scroll position."); }
    }
    /// <summary>Freezes the actual drawable for a boundary continuity render test without recording sound.</summary>
    internal void PreviewReactorAngleForSmoke(float? angle)
    { EnsureSmoke(); _smokeAngle = angle; if (angle.HasValue) { _core.Angle = angle.Value; _core.Level = 0; _reactor.Invalidate(); } }
    /// <summary>Exposes only the reactor's native visual to isolated pixel verification.</summary>
    internal Microsoft.UI.Xaml.UIElement ReactorVisualForSmoke()
    { EnsureSmoke(); return (Microsoft.UI.Xaml.UIElement)_reactor.Handler!.PlatformView!; }
    /// <summary>Reproduces queued scrolling after history replacement and verifies reading older messages.</summary>
    internal async Task VerifyConversationScrollingForSmokeAsync()
    {
        EnsureSmoke(); _conversationScroll.Follow();
        for (int index = 0; index < 205; index++)
        { AddMessage(new(ConversationMessageSource.Assistant, _ui.Get("ui.maui.listeningHelp"), DateTimeOffset.Now)); }
        if (_messages.Count != 200) { throw new InvalidOperationException("Conversation history must remain bounded."); }
        PreviewConversationForSmoke(); PreviewConversationForSmoke();
        await Task.Delay(300).ConfigureAwait(true);
        _chat.ScrollTo(0, position: ScrollToPosition.Start, animate: false); await Task.Delay(200).ConfigureAwait(true);
        _conversationScroll.ObserveManualForSmoke(100);
        AddMessage(new(ConversationMessageSource.Assistant, _ui.Get("ui.maui.listeningHelp"), DateTimeOffset.Now));
        await Task.Delay(150).ConfigureAwait(true);
        if (_conversationScroll.Following) { throw new InvalidOperationException("Reading earlier messages must not force scrolling to a new response."); }
        _conversationScroll.Follow(); PreviewConversationForSmoke(); await Task.Delay(200).ConfigureAwait(true);
    }
    /// <summary>Checks usable input geometry, accessible actions and actual microphone safety.</summary>
    internal void VerifyHudForSmoke()
    {
        EnsureSmoke();
        if (_live.IsListening) { throw new InvalidOperationException("UI verification must never capture microphone audio."); }
        if (_draft.Width < 120 || _chat.Height < 100 || _sendButton.Height < Theme.TargetSize || _activity.Width < 280)
        { throw new InvalidOperationException("The HUD must preserve readable chat and accessible input targets at every supported size."); }
        foreach (View action in new View[] { _listen, _microphoneButton, _interruptButton, _sendButton, _setupButton, _monitorButton })
        { if (string.IsNullOrWhiteSpace(SemanticProperties.GetDescription(action))) { throw new InvalidOperationException("Every icon action needs an accessible localized name."); } }
        if (_sendButton.IsEnabled != CanSend()) { throw new InvalidOperationException("Send action does not match draft availability."); }
    }
    /// <summary>Restricts diagnostic state changes to an explicit isolated rendering run.</summary>
    private static void EnsureSmoke()
    { if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("HUD previews require isolated UI smoke verification."); } }
    private void OnApprovalsChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(UpdateApprovals);
    private void UpdateApprovals()
    {
        int count = _services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().PendingRequests.Count;
        _approvalsButton.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Theme.DescribeAction(_approvalsButton, _ui.Get("ui.permissions.center") + " · " + count);
        _approvalsButton.BackgroundColor = count > 0 ? Theme.Primary : Theme.Field;
    }
    /// <inheritdoc />
    public void Dispose()
    {
        _services.GetRequiredService<OpenAiConversationEngine>().StreamUpdated -= ReceiveStream; _services.GetRequiredService<OpenAiConversationEngine>().Interrupted -= StreamInterrupted;
        var local = _services.GetRequiredService<Laura.Platform.Windows.Conversation.LlamaLocalInferenceEngine>(); local.StreamUpdated -= ReceiveStream; local.Interrupted -= StreamInterrupted;
        var voice = _services.GetRequiredService<LocalVoiceSession>(); voice.Changed -= LocalCaptureChanged; voice.Stop();
        _engine.ConversationMessageReceived -= ReceiveMessage; _live.MessageReceived -= ReceiveMessage; _live.StreamUpdated -= ReceiveStream; _live.Interrupted -= StreamInterrupted;
        _conversationScroll.Dispose(); _services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().Changed -= OnApprovalsChanged; _timer?.Stop(); _holdLifetime?.Cancel(); _holdLifetime?.Dispose(); _command?.Cancel(); GC.SuppressFinalize(this);
    }
}

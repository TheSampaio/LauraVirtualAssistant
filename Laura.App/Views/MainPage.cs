using System.Diagnostics;
using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Views;

/// <summary>Responsive three-column MAUI HUD with a vector reactor and native chat and microphone controls.</summary>
public sealed class MainPage : ContentPage, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAssistantEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IUiLocalizer _ui;
    private readonly GeminiLiveClient _live;
    private readonly AssistantCoreDrawable _core = new();
    private readonly GraphicsView _reactor;
    private readonly Grid _body = new() { ColumnSpacing = 18, RowSpacing = 18 };
    private readonly VerticalStackLayout _messages = new() { Spacing = 12 };
    private readonly ScrollView _chat;
    private readonly Entry _draft = Theme.Entry();
    private readonly Label _status = Theme.Label("Ready", 15, Theme.Accent);
    private readonly Label _provider = Theme.Label("", 12, Theme.Muted);
    private readonly Label _clock = Theme.Label("", 22, Theme.Accent);
    private readonly Label _metrics = Theme.Label("", 12, Theme.Muted);
    private readonly ResourceMeter _cpuMeter = new(Theme.Accent), _memoryMeter = new(Theme.Listening), _diskMeter = new(Color.FromArgb("#B594DF")), _networkMeter = new(Color.FromArgb("#65CDBD"));
    private readonly ResourceMeter _gpuMeter = new(Theme.Accent);
    private readonly Label _shortcutHint = Theme.Label("", 11, Theme.Muted);
    private readonly WindowsResourceMonitor _resources;
    private readonly Label _micStatus = Theme.Label("", 12, Theme.Muted);
    private readonly Button _listen;
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
    private readonly Button _setupButton;
    private readonly Button _memoryButton;
    private readonly Button _interruptButton;
    private readonly Button _microphoneButton;

    /// <summary>Composes the HUD and subscribes to domain and Live events through the UI dispatcher.</summary>
    public MainPage(IServiceProvider services, IAssistantEngine engine, ISettingsService settings, IUiLocalizer ui, GeminiLiveClient live, WindowsResourceMonitor resources)
    {
        _services = services; _engine = engine; _settings = settings; _ui = ui; _live = live; _resources = resources;
        BackgroundColor = Theme.Background;
        NavigationPage.SetHasNavigationBar(this, false);
        _reactor = new GraphicsView { Drawable = _core, MinimumHeightRequest = 300, VerticalOptions = LayoutOptions.Fill };
        _chat = new ScrollView { Content = _messages, VerticalOptions = LayoutOptions.Fill };
        _listen = Theme.Button("", ToggleListeningAsync);
        _monitorTitle = Theme.Caption(""); _coreTitle = Theme.Caption(""); _activityTitle = Theme.Caption("");
        _setupButton = Theme.Button("\uE713", OpenSetupAsync); _setupButton.FontFamily = "Segoe MDL2 Assets"; _setupButton.FontSize = 22; _setupButton.WidthRequest = 46; _setupButton.Padding = 4;
        SemanticProperties.SetDescription(_setupButton, ui.Get("ui.maui.setup"));
        _memoryButton = Theme.Button("", () => OpenPageAsync<MemoryPage>()); _interruptButton = Theme.Button("", InterruptAsync);
        _microphoneButton = Theme.Button("", ToggleMicrophoneAsync);
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Margin = new Thickness(0, 0, 0, 18) };
        header.Add(Theme.Stack(new Label { Text = "L A U R A", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Theme.Accent }, Theme.Label("PERSONAL AI ASSISTANT", 10, Theme.Muted)));
        var actions = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        actions.Add(_clock); actions.Add(_setupButton); header.Add(actions, 1);
        _monitor = Theme.Card(Theme.Stack(_monitorTitle, _cpuMeter, _memoryMeter, _diskMeter, _networkMeter, _gpuMeter, _metrics,
            Theme.Label("WINDOWS · .NET MAUI", 11, Theme.Muted), Theme.Label("Alt + L", 14, Theme.Accent)), 18);
        var centerGrid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        centerGrid.Add(_coreTitle); centerGrid.Add(_reactor, 0, 1);
        var footer = Theme.Stack(_status, _provider);
        var shortcuts = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        shortcuts.Add(_memoryButton);
        footer.Add(shortcuts); centerGrid.Add(footer, 0, 2); _center = Theme.Card(centerGrid);
        var chatGrid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 14 };
        chatGrid.Add(_activityTitle); chatGrid.Add(_chat, 0, 1);
        var compose = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        compose.Add(_draft); compose.Add(Theme.Button("➤", SendAsync, true), 1);
        _draft.Completed += async (_, _) => await SendAsync().ConfigureAwait(true);
        chatGrid.Add(Theme.Stack(compose, _listen, _interruptButton, _micStatus, _microphoneButton, _shortcutHint), 0, 2);
        _activity = Theme.Card(chatGrid);
        var root = new Grid { Padding = 22, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        root.Add(header); root.Add(_body, 0, 1); Content = root;
        SetLayout(false);
        SizeChanged += (_, _) => { bool narrow = Width < 1080; if (narrow != _narrow) { SetLayout(narrow); } };
        engine.ConversationMessageReceived += (_, message) => MainThread.BeginInvokeOnMainThread(() => AddMessage(message));
        engine.StateChanged += (_, state) => MainThread.BeginInvokeOnMainThread(() => SetState(state.ToString()));
        live.MessageReceived += (_, message) => MainThread.BeginInvokeOnMainThread(() => AddMessage(message));
        live.StatusChanged += (_, state) => MainThread.BeginInvokeOnMainThread(() => SetState(state));
        settings.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Reload);
    }
    /// <summary>Refreshes settings-driven labels and restores bounded history once.</summary>
    public void Reload()
    {
        _draft.Placeholder = _ui.Get("ui.hud.compose");
        _monitorTitle.Text = _ui.Get("ui.maui.monitor").ToUpperInvariant(); _coreTitle.Text = _ui.Get("ui.maui.core").ToUpperInvariant(); _activityTitle.Text = _ui.Get("ui.maui.activity").ToUpperInvariant();
        SemanticProperties.SetDescription(_setupButton, _ui.Get("ui.maui.setup")); ToolTipProperties.SetText(_setupButton, _ui.Get("ui.maui.setup")); _memoryButton.Text = _ui.Get("ui.tab.memory");
        _interruptButton.Text = _ui.Get("ui.maui.interrupt"); _microphoneButton.Text = _ui.Get("ui.maui.toggleMic");
        _provider.Text = "GEMINI LIVE · " + _settings.Current.GenerativeAi.LiveVoice;
        _listen.Text = _live.IsListening ? _ui.Get("ui.maui.stopRecording") : _ui.Get("ui.maui.startListening");
        _shortcutHint.Text = _ui.Get("ui.maui.shortcutHelp");
        _micStatus.Text = _settings.Current.MicrophoneMuted ? _ui.Get("ui.maui.muted") : _live.IsListening ? _ui.Get("ui.maui.streaming") : _ui.Get("ui.maui.micReady");
        if (!_loaded && _engine.State != AssistantState.Stopped) { _messages.Clear(); foreach (ConversationMessage message in _engine.ConversationHistory) { AddMessage(message); } _loaded = true; }
    }
    /// <summary>Opens the native settings workspace.</summary>
    public Task OpenSetupAsync() => OpenPageAsync<SetupPage>();
    /// <summary>Resolves a fresh detail page through the composition root.</summary>
    private Task OpenPageAsync<T>() where T : Page => Navigation.PushAsync(_services.GetRequiredService<T>());
    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing(); _pageVisible = true; Reload();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33); _timer.Tick -= Tick; _timer.Tick += Tick;
        if (_windowVisible) { _timer.Start(); }
    }
    /// <inheritdoc />
    protected override void OnDisappearing() { _pageVisible = false; _timer?.Stop(); base.OnDisappearing(); }
    /// <summary>Suspends vector animation while the native window is hidden in the tray.</summary>
    public void SetWindowVisible(bool visible)
    { _windowVisible = visible; if (visible && _pageVisible) { _timer?.Start(); } else { _timer?.Stop(); } }
    /// <summary>Animates vector rings and refreshes measured system statistics at one-second intervals.</summary>
    private void Tick(object? sender, EventArgs args)
    {
        _core.Mode = _smokeMode ?? (_live.IsSpeaking ? ReactorMode.Speaking : _live.IsListening ? ReactorMode.Listening : _engine.State == AssistantState.Working ? ReactorMode.Working : ReactorMode.Idle);
        _core.Level += ((_core.Mode == ReactorMode.Listening ? _live.InputLevel : _live.OutputLevel) - _core.Level) * .25f;
        _core.Angle = (_core.Angle + (_core.Active ? 1.2f : .45f)) % 360;
        _status.TextColor = _core.Mode == ReactorMode.Listening ? Theme.Listening : Theme.Speaking;
        if (_core.Mode is ReactorMode.Listening or ReactorMode.Speaking) { _status.Text = _ui.Get(_core.Mode == ReactorMode.Listening ? "ui.maui.listening" : "ui.state.speaking"); }
        _reactor.Invalidate();
        if (Environment.TickCount64 < _nextMetrics) { return; }
        _nextMetrics = Environment.TickCount64 + 1000;
        _clock.Text = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        ResourceSnapshot metrics = _resources.Sample(AppContext.BaseDirectory);
        _cpuMeter.Update("CPU", $"{metrics.Cpu:P0}", metrics.Cpu);
        _memoryMeter.Update(_ui.Get("ui.maui.ram"), $"{metrics.UsedMemory / 1073741824d:F1} / {metrics.TotalMemory / 1073741824d:F1} GB", metrics.Memory);
        _diskMeter.Update(_ui.Get("ui.maui.disk"), $"{metrics.Disk:P0} · {metrics.FreeDisk / 1073741824d:F0} GB " + _ui.Get("ui.maui.free"), metrics.Disk);
        _networkMeter.Update(_ui.Get("ui.maui.network"), metrics.NetworkConnected ? $"↓ {metrics.DownloadBytesPerSecond / 1024:F0}  ↑ {metrics.UploadBytesPerSecond / 1024:F0} KB/s" : _ui.Get("ui.maui.offline"), metrics.Network);
        _gpuMeter.Update("GPU", metrics.Gpu is { } usage ? $"{usage:P0}" : _ui.Get("ui.maui.unavailable"), metrics.Gpu ?? 0);
        _metrics.Text = _ui.Get("ui.maui.networkHint") + $"\n\nLAURA · {metrics.AppMemory / 1048576d:F0} MB\n{metrics.Uptime:hh\\:mm\\:ss}";
    }
    /// <summary>Adapts the HUD to compact windows without overflowing the chat or settings controls.</summary>
    private void SetLayout(bool narrow)
    {
        _narrow = narrow; _body.Children.Clear(); _body.ColumnDefinitions.Clear(); _body.RowDefinitions.Clear();
        _monitor.IsVisible = !narrow;
        if (narrow)
        {
            _body.ColumnDefinitions.Add(new(GridLength.Star)); _body.ColumnDefinitions.Add(new(GridLength.Star));
            _body.Add(_center, 0, 0); _body.Add(_activity, 1, 0);
        }
        else
        {
            _body.ColumnDefinitions.Add(new(new GridLength(205))); _body.ColumnDefinitions.Add(new(GridLength.Star)); _body.ColumnDefinitions.Add(new(new GridLength(360)));
            _body.Add(_monitor, 0, 0); _body.Add(_center, 1, 0); _body.Add(_activity, 2, 0);
        }
    }
    /// <summary>Adds a bounded chat bubble and scrolls it into view.</summary>
    private void AddMessage(ConversationMessage message)
    {
        var label = Theme.Label(message.Text, 14);
        var bubble = Theme.Card(Theme.Stack(label, Theme.Label(message.Timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), 10, Theme.Muted)), 12);
        bubble.BackgroundColor = message.Source == ConversationMessageSource.Assistant ? Theme.Field : Color.FromArgb("#631829");
        bubble.Margin = message.Source == ConversationMessageSource.Assistant ? new Thickness(0, 0, 20, 0) : new Thickness(20, 0, 0, 0);
        _messages.Add(bubble); if (_messages.Count > 200) { _messages.RemoveAt(0); }
        Dispatcher.Dispatch(() => _ = _chat.ScrollToAsync(0, _messages.Height, true));
    }
    /// <summary>Updates state feedback and the core's animation speed.</summary>
    private void SetState(string state)
    {
        _status.Text = state switch { "Ready" => _ui.Get("ui.state.ready"), "Speaking" => _ui.Get("ui.state.speaking"), "Working" => _ui.Get("ui.state.working"), "Listening" => _ui.Get("ui.maui.listening"), _ => state };
        Reload();
    }
    /// <summary>Submits a text command through the existing assistant queue.</summary>
    private async Task SendAsync()
    {
        string text = _draft.Text?.Trim() ?? "";
        if (text.Length == 0 || _command is not null) { return; }
        _draft.Text = ""; _command = new CancellationTokenSource();
        try { await _engine.SubmitCommandAsync(text, _command.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        finally { _command.Dispose(); _command = null; }
    }
    /// <summary>Toggles automatic turn detection; a natural speech pause triggers a response without another click.</summary>
    private async Task ToggleListeningAsync()
    {
        if (_settings.Current.MicrophoneMuted) { await DisplayAlertAsync("Laura", _ui.Get("ui.maui.unmuteFirst"), "OK").ConfigureAwait(true); return; }
        if (_live.IsListening) { await _live.StopListeningAsync().ConfigureAwait(true); }
        else { await _live.StartListeningAsync().ConfigureAwait(true); }
        Reload();
    }
    /// <summary>Stops microphone capture before persisting its muted state.</summary>
    public async Task ToggleMicrophoneAsync()
    {
        if (!_settings.Current.MicrophoneMuted) { _holdLifetime?.Cancel(); await _live.StopListeningAsync().ConfigureAwait(true); }
        await _settings.UpdateAsync(_settings.Current with { MicrophoneMuted = !_settings.Current.MicrophoneMuted }).ConfigureAwait(true);
    }
    /// <summary>Cancels microphone input, native generation, local generation and playback.</summary>
    public async Task InterruptAsync()
    {
        _holdLifetime?.Cancel(); _command?.Cancel(); _live.Interrupt(); _engine.StopSpeaking(); await _live.StopListeningAsync().ConfigureAwait(true); Reload();
    }
    /// <summary>Captures only while the global Ctrl+Space chord is held, including connection cancellation on release.</summary>
    public async Task SetPushToTalkAsync(bool held)
    {
        if (!held)
        { var lifetime = _holdLifetime; _holdLifetime = null; lifetime?.Cancel(); await _live.StopListeningAsync().ConfigureAwait(true); lifetime?.Dispose(); Reload(); return; }
        if (_settings.Current.MicrophoneMuted) { _micStatus.Text = _ui.Get("ui.maui.unmuteFirst"); return; }
        _holdLifetime?.Cancel(); _holdLifetime?.Dispose(); var capture = new CancellationTokenSource(); _holdLifetime = capture;
        CancellationToken token = capture.Token;
        try { _live.Interrupt(); await _live.StopListeningAsync().ConfigureAwait(true); token.ThrowIfCancellationRequested(); await _live.StartListeningAsync(pushToTalk: true, cancellationToken: token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception) { _micStatus.Text = _ui.Get("ui.maui.liveFailed"); }
        Reload();
    }
    /// <summary>Previews colors for the isolated rendering harness without recording or playing sound.</summary>
    public void PreviewReactorForSmoke(ReactorMode? mode)
    { if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Visual preview is restricted to isolated smoke verification."); } _smokeMode = mode; _core.Level = .5f; }
    /// <inheritdoc />
    public void Dispose() { _timer?.Stop(); _holdLifetime?.Cancel(); _holdLifetime?.Dispose(); _command?.Cancel(); GC.SuppressFinalize(this); }
}

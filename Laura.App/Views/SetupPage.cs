using System.Text.Json;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
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
    private readonly IUiLocalizer _ui;
    private readonly IStartupRegistration _startup;
    private readonly ISpeechSynthesizer _speech;
    private readonly ILocalizer _conversation;
    private readonly WindowsApplicationCatalog _applications;
    private readonly Picker _voice, _culture, _uiCulture, _input, _output;
    private readonly Entry _key = Theme.Entry(), _model = Theme.Entry(), _nickname = Theme.Entry(), _appSearch = Theme.Entry();
    private readonly Editor _persona = new() { TextColor = Theme.Text, BackgroundColor = Theme.Field, HeightRequest = 120 };
    private readonly Switch _muted, _remember, _greeting, _hourly, _autoStart;
    private readonly Slider _volume, _silence;
    private readonly int[] _inputIds, _outputIds;
    private readonly Label _keyState = Theme.Label("", 12, Theme.Muted), _result = Theme.Label("", 13, Theme.Listening);
    private readonly Label _silenceValue = Theme.Label("", 12, Theme.Muted);
    private readonly VerticalStackLayout _appList = new() { Spacing = 8 };
    private readonly View[] _pages;
    private readonly Button[] _tabs;
    private readonly ContentView _active = new();

    /// <summary>Creates independent tabs with a fixed save footer and no offline provider controls.</summary>
    public SetupPage(ISettingsService settings, IApiKeyStore keys, GeminiLiveClient live, IUiLocalizer ui,
        IStartupRegistration startup, ISpeechSynthesizer speech, WindowsApplicationCatalog applications, ILocalizer conversation)
    {
        _settings = settings; _keys = keys; _live = live; _ui = ui; _startup = startup; _speech = speech; _applications = applications; _conversation = conversation;
        LauraSettings current = settings.Current;
        Title = ui.Get("ui.maui.setup"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        _voice = Theme.Picker(GeminiLiveOptions.Voices, current.GenerativeAi.LiveVoice);
        _culture = Theme.Picker(["pt-BR", "en-US"], current.Culture); _uiCulture = Theme.Picker(["pt-BR", "en-US"], current.UiCulture);
        _model.Text = string.IsNullOrWhiteSpace(current.GenerativeAi.Model) ? GeminiLiveOptions.Model : current.GenerativeAi.Model;
        _nickname.Text = current.UserNickname; _persona.Text = current.GenerativeAi.Persona;
        _key.IsPassword = true; _key.Placeholder = ui.Get("ui.maui.keyPlaceholder");
        _muted = Toggle(current.MicrophoneMuted); _remember = Toggle(current.RememberConversations);
        _greeting = Toggle(current.GreetOnStartup); _hourly = Toggle(current.AnnounceHourly); _autoStart = Toggle(current.StartWithWindows);
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = current.Voice.Volume, MinimumTrackColor = Theme.Accent, ThumbColor = Theme.Accent };
        _silence = new Slider { Minimum = 300, Maximum = 2000, Value = current.GenerativeAi.SilenceDurationMs, MinimumTrackColor = Theme.Listening, ThumbColor = Theme.Listening };
        _silence.ValueChanged += (_, _) => UpdateSilence(); UpdateSilence();
        _inputIds = [-1, .. Enumerable.Range(0, WaveInEvent.DeviceCount)];
        _outputIds = [-1, .. Enumerable.Range(0, WaveInterop.waveOutGetNumDevs())];
        string[] inputs = [ui.Get("ui.maui.systemDefault"), .. _inputIds.Skip(1).Select(id => WaveInEvent.GetCapabilities(id).ProductName)];
        string[] outputs = [ui.Get("ui.maui.systemDefault"), .. _outputIds.Skip(1).Select(OutputName)];
        _input = Theme.Picker(inputs, inputs[Math.Max(0, Array.IndexOf(_inputIds, current.InputDevice))]);
        _output = Theme.Picker(outputs, outputs[Math.Max(0, Array.IndexOf(_outputIds, current.OutputDevice))]);
        _appSearch.Placeholder = ui.Get("ui.maui.searchApps"); _appSearch.TextChanged += (_, _) => RefreshApplications();
        _pages = [
            Panel(Theme.Caption("GOOGLE AI STUDIO"), Theme.Label(ui.Get("ui.maui.googleIntro")), Row("ui.maui.key", _key), _keyState,
                Theme.Button(ui.Get("ui.maui.importMark"), ImportMarkAsync), Theme.Button(ui.Get("ui.maui.removeKey"), RemoveKeyAsync),
                Row("ui.ai.model", _model), Theme.Label(ui.Get("ui.maui.cloudPrivacy"), 12, Theme.Muted)),
            Panel(Row("ui.maui.liveVoice", _voice), Theme.Label(ui.Get("ui.maui.brazilianAccent"), 12, Theme.Listening),
                Row("ui.maui.input", _input), Row("ui.maui.output", _output), Row("ui.voice.volume", _volume),
                Row("ui.maui.silence", _silence), _silenceValue, Row("ui.maui.muted", _muted),
                Theme.Label(ui.Get("ui.maui.listeningHelp"), 12, Theme.Muted), Theme.Button(ui.Get("ui.maui.savePreview"), PreviewAsync)),
            Panel(Row("ui.maui.uiLanguage", _uiCulture), Row("ui.maui.language", _culture), Row("ui.maui.nickname", _nickname),
                Row("ui.memory.remember", _remember), Row("ui.maui.greeting", _greeting), Row("ui.maui.hourly", _hourly),
                Row("ui.maui.autostart", _autoStart), Row("ui.ai.persona", _persona)),
            Panel(Theme.Label(ui.Get("ui.maui.appsIntro"), 13, Theme.Muted), _appSearch,
                Theme.Button(ui.Get("ui.maui.refreshApps"), RefreshApplicationsAsync), _appList)
        ];
        string[] names = ["ui.maui.connectionTab", "ui.tab.voice", "ui.maui.preferencesTab", "ui.maui.appsTab"];
        _tabs = names.Select((name, index) => Theme.Button(ui.Get(name), () => { SelectTab(index); return Task.CompletedTask; })).ToArray();
        var tabs = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        for (int index = 0; index < _tabs.Length; index++) { tabs.Add(_tabs[index], index); }
        var footer = Theme.Stack(_result, Theme.Button(ui.Get("ui.maui.save"), async () => { await SaveAsync().ConfigureAwait(true); }, true));
        var body = new Grid { Padding = new Thickness(26, 0, 26, 22), RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 16, MaximumWidthRequest = 960, HorizontalOptions = LayoutOptions.Fill };
        body.Add(tabs); body.Add(_active, 0, 1); body.Add(footer, 0, 2);
        Content = Theme.Workspace(Title, "LAURA", body, async () => { await Navigation.PopAsync().ConfigureAwait(true); });
        SelectTab(0); RefreshKeyState(); RefreshApplications();
    }
    /// <summary>Selects a single tab while preserving unsaved values.</summary>
    public void SelectTab(int index)
    {
        if (index < 0 || index >= _pages.Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
        _active.Content = _pages[index];
        for (int item = 0; item < _tabs.Length; item++) { _tabs[item].BackgroundColor = item == index ? Theme.Accent : Theme.Field; }
    }
    /// <summary>Creates a scrollable tab panel.</summary>
    private static View Panel(params View[] children) => new ScrollView { Content = Theme.Card(Theme.Stack(children), 22) };
    /// <summary>Creates an accent switch.</summary>
    private static Switch Toggle(bool value) => new() { IsToggled = value, OnColor = Theme.Accent, HorizontalOptions = LayoutOptions.Start };
    /// <summary>Reads playback labels without Windows speech libraries.</summary>
    private static string OutputName(int id)
    { NAudio.MmException.Try(WaveInterop.waveOutGetDevCaps((nint)id, out WaveOutCapabilities capabilities, System.Runtime.InteropServices.Marshal.SizeOf<WaveOutCapabilities>()), "waveOutGetDevCaps"); return capabilities.ProductName; }
    /// <summary>Creates a compact labeled field.</summary>
    private View Row(string key, View view) => Theme.Stack(Theme.Label(_ui.Get(key), 12, Theme.Muted), view);
    /// <summary>Displays speech-end delay in milliseconds.</summary>
    private void UpdateSilence() => _silenceValue.Text = $"{_silence.Value:F0} ms";
    /// <summary>Shows a fixed mask for a stored credential without placing the secret or a fake key in editable text.</summary>
    private void RefreshKeyState()
    {
        bool saved = !string.IsNullOrWhiteSpace(_keys.Read());
        _key.Placeholder = saved ? "••••••••••••••••" : _ui.Get("ui.maui.keyPlaceholder");
        _key.PlaceholderColor = saved ? Theme.Text : Theme.Muted;
        _keyState.Text = _ui.Get(saved ? "ui.maui.keySaved" : "ui.maui.noKey");
    }
    /// <summary>Refreshes application discovery in the background.</summary>
    private async Task RefreshApplicationsAsync() { await _applications.RefreshAsync().ConfigureAwait(true); RefreshApplications(); }
    /// <summary>Shows names and targets that Laura can actually launch.</summary>
    private void RefreshApplications()
    {
        _appList.Clear();
        var apps = string.IsNullOrWhiteSpace(_appSearch.Text) ? _applications.Applications : _applications.Find(_appSearch.Text);
        foreach (InstalledApplication app in apps.Take(60)) { _appList.Add(Theme.Stack(Theme.Label(app.Name, 14), Theme.Label(app.Target, 11, Theme.Muted))); }
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
    private async Task RemoveKeyAsync() { await _live.ReconfigureAsync().ConfigureAwait(true); _keys.Save(""); _key.Text = ""; RefreshKeyState(); }
    /// <summary>Saves all tabs through the existing settings service.</summary>
    private async Task<bool> SaveAsync()
    {
        string? candidate = string.IsNullOrWhiteSpace(_key.Text) ? null : _key.Text.Trim();
        if (candidate is null && string.IsNullOrWhiteSpace(_keys.Read())) { _result.Text = _ui.Get("ui.maui.noKey"); return false; }
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
        await _live.ReconfigureAsync().ConfigureAwait(true);
        if (candidate is not null) { _keys.Save(candidate); if (_key.Text?.Trim() == candidate) { _key.Text = ""; } }
        LauraSettings updated = _settings.Current with
        {
            Culture = _culture.SelectedItem as string ?? "pt-BR", UiCulture = _uiCulture.SelectedItem as string ?? "pt-BR",
            UserNickname = _nickname.Text ?? "", MicrophoneMuted = _muted.IsToggled, RememberConversations = _remember.IsToggled,
            InputDevice = _inputIds[Math.Max(0, _input.SelectedIndex)], OutputDevice = _outputIds[Math.Max(0, _output.SelectedIndex)],
            GreetOnStartup = _greeting.IsToggled, AnnounceHourly = _hourly.IsToggled, StartWithWindows = _autoStart.IsToggled,
            Voice = _settings.Current.Voice with { VoiceName = null, Volume = (int)_volume.Value },
            GenerativeAi = _settings.Current.GenerativeAi with { Enabled = true, Provider = "gemini", Model = _model.Text ?? "", Persona = (_persona.Text ?? "").ReplaceLineEndings(" "), LiveVoice = _voice.SelectedItem as string ?? "Aoede", ContinuousListening = true, SilenceDurationMs = (int)_silence.Value }
        };
        await _settings.UpdateAsync(updated).ConfigureAwait(true); _ui.SetCulture(updated.ResolveUiCulture());
        if (_startup.IsEnabled() != updated.StartWithWindows) { _startup.SetEnabled(updated.StartWithWindows); }
        RefreshKeyState(); _result.Text = _ui.Get("ui.maui.saved"); return true;
    }
    /// <summary>Previews Gemini after applying the language and voice settings.</summary>
    private async Task PreviewAsync()
    {
        if (!await SaveAsync().ConfigureAwait(true)) { return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        string preview = _conversation.Get("ui.voice.previewText");
        await _speech.SpeakAsync(new SpeechRequest(preview, _settings.Current.Voice, _settings.Current.Culture), timeout.Token).ConfigureAwait(true);
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

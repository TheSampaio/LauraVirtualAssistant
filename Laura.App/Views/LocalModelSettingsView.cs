using System.Security.Cryptography;
using System.Text.Json;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Models;
using Laura.Platform.Windows.Conversation;

namespace Laura.App.Views;

/// <summary>Edits local runtime/voice settings and installs only an explicitly reviewed optional pack.</summary>
public sealed class LocalModelSettingsView : ContentView
{
    private readonly IUiLocalizer _ui;
    private readonly LocalPackInstaller _installer;
    private readonly ISettingsService _settings;
    private readonly Switch _enabled = new() { OnColor = Theme.Accent };
    private readonly Entry _executable = Theme.Entry(), _model = Theme.Entry(), _kokoro = Theme.Entry(), _whisper = Theme.Entry();
    private readonly Entry _context = Theme.Entry(), _threads = Theme.Entry();
    private readonly Switch _detectLanguage = new() { OnColor = Theme.Accent };
    private readonly Picker _difficulty = Theme.Picker(["Light", "Standard"], "Light");
    private readonly Label _status = Theme.Label("", 12, Theme.Muted);
    private readonly Button _install, _verify, _cancel;
    private CancellationTokenSource? _operation;
    private LocalModelOptions _baseline;
    private LocalVoiceOptions _voice;
    /// <summary>Creates local configuration without downloading assets, starting processes or opening a microphone.</summary>
    public LocalModelSettingsView(IUiLocalizer ui, LocalPackInstaller installer, ISettingsService settingsService)
    {
        _ui = ui; _installer = installer; _settings = settingsService; var settings = settingsService.Current; _baseline = settings.LocalModel; _voice = settings.LocalVoice;
        _install = Theme.ActionButton(T("install"), "box", InstallAsync);
        _verify = Theme.ActionButton(T("verify"), "shield_check", VerifyAsync);
        _cancel = Theme.ActionButton(T("cancel"), "x", () => { _operation?.Cancel(); return Task.CompletedTask; }); _cancel.IsEnabled = false; _cancel.IsVisible = false;
        var advanced = Theme.Stack(
            Field("executable", _executable), Field("model", _model), _verify,
            Field("context", _context), Field("threads", _threads), Theme.Label(T("difficulty"), 13), _difficulty,
            Field("kokoro", _kokoro), Field("whisper", _whisper)); advanced.IsVisible = false;
        Content = Theme.Stack(Theme.Label(T("title"), 22), Theme.Label(T("intro"), 13, Theme.Muted), Theme.Label(T("enabled"), 13), _enabled,
            Theme.Label(T("installReview", LocalModelPackages.DownloadBytes / 1_000_000_000d, LocalModelPaths.Models), 12, Theme.Muted), _install, _status, _cancel,
            Theme.Label(T("gpu"), 13), Theme.Label(T("detectLanguage"), 13), _detectLanguage,
            Theme.ActionButton(T("advanced"), "settings_2", () => { advanced.IsVisible = !advanced.IsVisible; return Task.CompletedTask; }), advanced);
        Reload(settings); Unloaded += (_, _) => _operation?.Cancel();
    }
    /// <summary>Gets immutable local model edits; changing a path invalidates its previous integrity pin until explicit verification.</summary>
    public LocalModelOptions Options => (_baseline with
    {
        Enabled = _enabled.IsToggled, Executable = _executable.Text ?? "", ModelPath = _model.Text ?? "",
        ExecutableHash = _executable.Text == _baseline.Executable ? _baseline.ExecutableHash : "", ModelHash = _model.Text == _baseline.ModelPath ? _baseline.ModelHash : "",
        ContextTokens = Integer(_context.Text, 8192), Threads = Integer(_threads.Text, 2), GpuLayers = -1,
        MaximumDifficulty = _difficulty.SelectedIndex == 1 ? TaskDifficulty.Standard : TaskDifficulty.Light
    }).Sanitized();
    /// <summary>Gets provider-independent local voice installation paths.</summary>
    public LocalVoiceOptions VoiceOptions => (_voice with { KokoroDirectory = _kokoro.Text ?? "", WhisperDirectory = _whisper.Text ?? "", DetectLanguage = _detectLanguage.IsToggled }).Sanitized();
    /// <summary>Restores saved values without carrying an incomplete installation into active settings.</summary>
    public void Reload(LauraSettings settings)
    {
        _baseline = settings.LocalModel; _voice = settings.LocalVoice; _enabled.IsToggled = _baseline.Enabled;
        _executable.Text = _baseline.Executable; _model.Text = _baseline.ModelPath; _kokoro.Text = _voice.KokoroDirectory; _whisper.Text = _voice.WhisperDirectory;
        _context.Text = _baseline.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture); _threads.Text = _baseline.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _difficulty.SelectedIndex = _baseline.MaximumDifficulty == TaskDifficulty.Standard ? 1 : 0; _detectLanguage.IsToggled = _voice.DetectLanguage;
        _status.Text = _baseline.ExecutableHash.Length == 64 && File.Exists(_baseline.Executable) && File.Exists(_baseline.ModelPath) && File.Exists(Path.Combine(_voice.KokoroDirectory, "kokoro-model.onnx")) && File.Exists(Path.Combine(_voice.WhisperDirectory, "base-encoder.int8.onnx")) ? T("ready") : "";
    }
    /// <summary>Shows one translated configuration field.</summary>
    private View Field(string key, Entry entry) => Theme.Stack(Theme.Label(T(key), 13), entry);
    /// <summary>Installs the displayed reviewed pack from the explicit native Install action and saves verified paths automatically.</summary>
    private async Task InstallAsync()
    {
        string parent = LocalModelPaths.Models;
        await RunAsync(async token =>
        {
            var progress = new Progress<LocalPackInstaller.Progress>(value => _status.Text = value.Component + " · " + (value.Received * 100d / value.Total).ToString("F0", _ui.Culture) + "%");
            var installed = await Task.Run(() => _installer.InstallAsync(parent, progress, token), token).ConfigureAwait(true);
            await _settings.UpdateAsync(_settings.Current with { LocalModel = installed.Model, LocalVoice = installed.Voice }, token).ConfigureAwait(true);
            Reload(_settings.Current);
            _status.Text = T("installed");
        }).ConfigureAwait(true);
    }
    /// <summary>Pins explicitly selected local executable/weights and adjacent native libraries after review; it does not execute them.</summary>
    private async Task VerifyAsync()
    {
        string executable = _executable.Text ?? "", model = _model.Text ?? "";
        if (!File.Exists(executable) || !File.Exists(model)) { _status.Text = T("missing"); return; }
        if (!await Laura.App.Platforms.Windows.WindowsConfirmation.AskAsync(T("importReview", executable, model), T("verify"), T("cancel"), CancellationToken.None).ConfigureAwait(true)) { return; }
        await RunAsync(async token =>
        {
            await using (var weights = File.OpenRead(model)) { byte[] magic = new byte[4]; await weights.ReadExactlyAsync(magic, token).ConfigureAwait(true); if (!magic.AsSpan().SequenceEqual("GGUF"u8)) { throw new InvalidDataException(); } }
            var libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dll in Directory.GetFiles(Path.GetDirectoryName(executable)!, "*.dll")) { libraries[Path.GetFileName(dll)] = await HashAsync(dll, token).ConfigureAwait(true); }
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(executable)!, "laura-runtime.json"), JsonSerializer.Serialize(libraries), token).ConfigureAwait(true);
            _baseline = Options with { Executable = executable, ModelPath = model, ExecutableHash = await HashAsync(executable, token).ConfigureAwait(true), ModelHash = await HashAsync(model, token).ConfigureAwait(true) };
            _status.Text = T("verified");
        }).ConfigureAwait(true);
    }
    /// <summary>Serializes native installation actions with cancellation and bounded localized error presentation.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) { return; }
        using var operation = new CancellationTokenSource(); _operation = operation; _install.IsEnabled = _verify.IsEnabled = false; _cancel.IsEnabled = true; _cancel.IsVisible = true;
        try { await action(operation.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { _status.Text = T("canceled"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        { _status.Text = T("failed"); }
        finally { _operation = null; _install.IsEnabled = _verify.IsEnabled = true; _cancel.IsEnabled = false; _cancel.IsVisible = false; }
    }
    /// <summary>Computes integrity metadata without reading a model into managed memory.</summary>
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)); }
    /// <summary>Parses only invariant integer resource budgets.</summary>
    private static int Integer(string? value, int fallback) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : fallback;
    /// <summary>Resolves centralized local-runtime resources.</summary>
    private string T(string key, params object[] args) => _ui.Get("ui.local." + key, args);
}

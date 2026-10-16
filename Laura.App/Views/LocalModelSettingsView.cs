using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Models;

namespace Laura.App.Views;

/// <summary>Native managed-weight controls with explicit download, cancellation and lifecycle status.</summary>
public sealed class LocalModelSettingsView : ContentView
{
    private readonly ILocalModelManager _models;
    private readonly ILocalInferenceEngine _inference;
    private readonly IUiLocalizer _ui;
    private readonly Picker _model;
    private readonly Picker _backend;
    private static readonly int[] GpuProfiles = [-1, 0, 16, 99];
    private readonly ModelArtifact[] _catalogue;
    private readonly Label _state = Theme.Label("", 12, Theme.Muted);
    private readonly Label _hardware = Theme.Label("", 12, Theme.Muted);
    private readonly IModelHardwareProbe _hardwareProbe;
    private HardwareProfile? _profile;
    private CancellationTokenSource? _hardwareOperation;
    private Task? _hardwareTask;
    private CancellationTokenSource? _operation;

    /// <summary>Creates isolated model actions. Args: models: Reviewed downloads; inference: Native lifecycle; settings: Current selection; ui: Central resources.</summary>
    public LocalModelSettingsView(ILocalModelManager models, ILocalInferenceEngine inference, ISettingsService settings, IUiLocalizer ui, IModelHardwareProbe hardware)
    {
        _models = models; _inference = inference; _ui = ui; _hardwareProbe = hardware; _catalogue = models.Catalog.Where(model => !model.IsSpeechModel).ToArray();
        _model = Theme.Picker(_catalogue.Select(model => model.Name), _catalogue.First(model => model.Id == settings.Current.GenerativeAi.LocalModel).Name);
        _backend = Theme.Picker(new[] { T("backendAuto"), T("backendCpu"), T("backendPartial"), T("backendFull") }, "");
        _backend.SelectedIndex = Math.Max(0, Array.IndexOf(GpuProfiles, settings.Current.GenerativeAi.LocalGpuLayers));
        _model.SelectedIndexChanged += (_, _) => Refresh();
        Content = Theme.Stack(Theme.Label(T("localTitle"), 22), _model, Theme.Label(T("localHelp"), 12, Theme.Muted),
            Theme.Label(T("backendSelection"), 14), _backend, Theme.Label(T("backendHelp"), 12, Theme.Muted), _state,
            Theme.ActionButton(T("install"), "upload", InstallAsync), Theme.ActionButton(T("load"), "play", () => RunAsync(token => _inference.LoadAsync(ModelId, token))),
            Theme.ActionButton(T("installSpeech"), "mic", () => RunAsync(token => _models.InstallAsync(SupportedModels.Speech, null, token))),
            Theme.ActionButton(T("unload"), "square", () => RunAsync(_inference.UnloadAsync)),
            Theme.ActionButton(T("cancel"), "x", () => { _operation?.Cancel(); _inference.Cancel(); return Task.CompletedTask; }),
            Theme.Label(T("hardwareTitle"), 18), _hardware, Theme.Label(T("cpuBackend"), 12, Theme.Muted),
            Theme.ActionButton(T("refreshHardware"), "refresh-cw", RefreshHardwareAsync),
            Theme.ActionButton(T("smallerModel"), "chevron-down", SelectSmallerModelAsync));
        Loaded += async (_, _) => { _inference.LoadingProgress += OnLoadProgress; await RefreshHardwareAsync().ConfigureAwait(true); };
        Unloaded += (_, _) => { _operation?.Cancel(); _hardwareOperation?.Cancel(); _inference.LoadingProgress -= OnLoadProgress; }; Refresh();
    }

    /// <summary>Gets the selected reviewed model ID; the existing settings Save action persists it.</summary>
    public string ModelId => _catalogue[Math.Max(0, _model.SelectedIndex)].Id;
    /// <summary>Gets the explicit native backend draft; Save persists the selected reviewed profile.</summary>
    public int GpuLayersPreference => GpuProfiles[Math.Max(0, _backend.SelectedIndex)];
    /// <summary>Restores the saved selection when settings reopen.</summary>
    public void Reload(string modelId, int gpuLayers) { _model.SelectedIndex = Array.FindIndex(_catalogue, model => model.Id == modelId); _backend.SelectedIndex = Math.Max(0, Array.IndexOf(GpuProfiles, gpuLayers)); Refresh(); }

    /// <summary>Downloads only the selected pinned artifact and preserves the partial file when cancelled.</summary>
    private Task InstallAsync() => RunAsync(token => _models.InstallAsync(_catalogue[Math.Max(0, _model.SelectedIndex)], new Progress<ModelDownloadProgress>(progress =>
    { if (_operation is not null) { _state.Text = progress.Verifying ? T("verifying") : string.Format(System.Globalization.CultureInfo.CurrentCulture, T("downloadProgress"), progress.BytesReceived / 1048576, progress.TotalBytes / 1048576); } }), token));

    /// <summary>Serializes local actions and reports a safe failure without claiming successful loading.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) { return; }
        using var operation = new CancellationTokenSource(); _operation = operation; _state.Text = T("localWorking");
        try { await action(operation.Token).ConfigureAwait(true); Refresh(); }
        catch (OperationCanceledException) { _state.Text = T("cancelled"); }
        catch (Exception error)
        { _state.Text = T("localFailed") + " · " + _inference.Status + " · " + error.GetType().Name; }
        finally { if (ReferenceEquals(_operation, operation)) { _operation = null; } }
    }

    /// <summary>Displays installed state and the actual native lifecycle without loading any model.</summary>
    private void Refresh()
    {
        _state.Text = (_models.GetInstalledModels().Contains(ModelId, StringComparer.Ordinal) ? T("installed") : T("missing"))
            + " · " + T("state." + _inference.Status) + " · " + T(File.Exists(_models.GetModelPath(SupportedModels.Speech.Id)) ? "speechInstalled" : "speechMissing")
            + "\n" + FormatHardware("backendActual", T("backend." + _inference.Backend), _inference.OffloadedLayers)
            + (_inference.Backend == "unloaded" ? "" : "\n" + T("backendStatus." + _inference.BackendStatus));
        RefreshHardwareSummary();
    }

    /// <summary>Observes hardware asynchronously and discards results after navigation or a newer refresh.</summary>
    private Task RefreshHardwareAsync()
    {
        if (_hardwareTask is { IsCompleted: false } && _hardwareOperation?.IsCancellationRequested == false) { return _hardwareTask; }
        return _hardwareTask = ObserveHardwareAsync();
    }

    /// <summary>Runs the shared observation once so page loading and manual refresh cannot cancel one another.</summary>
    private async Task ObserveHardwareAsync()
    {
        _hardwareOperation?.Cancel(); using var operation = new CancellationTokenSource(); _hardwareOperation = operation;
        _hardware.Text = T("checkingHardware");
        try
        {
            HardwareProfile profile = await _hardwareProbe.ReadAsync(operation.Token).ConfigureAwait(true);
            operation.Token.ThrowIfCancellationRequested(); _profile = profile; RefreshHardwareSummary();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!operation.IsCancellationRequested) { _hardware.Text = T("hardwareUnavailable"); } }
        finally { if (ReferenceEquals(_hardwareOperation, operation)) { _hardwareOperation = null; } }
    }

    /// <summary>Shows measured capacity separately from conservative admission and unverified performance requirements.</summary>
    private void RefreshHardwareSummary()
    {
        if (_profile is not { } profile) { return; }
        var model = _catalogue[Math.Max(0, _model.SelectedIndex)]; const double gib = 1073741824d;
        var lines = new List<string>
        {
            FormatHardware("hardwareCpu", profile.Architecture, profile.PhysicalCores?.ToString(_ui.Culture) ?? T("hardwareUnknown"), profile.Processors,
                profile.Instructions.Count == 0 ? T("hardwareUnknown") : string.Join(", ", profile.Instructions)),
            FormatHardware("hardwareRam", profile.AvailableRam / gib, profile.TotalRam / gib, profile.UsedRam / gib),
            FormatHardware("hardwareDisk", profile.FreeDisk / gib),
        };
        if (!profile.GpuDiscoveryAvailable) { lines.Add(T("hardwareGpuUnknown")); }
        else if (profile.GpuAdapters.Count == 0) { lines.Add(T("hardwareGpuNone")); }
        foreach (var gpu in profile.GpuAdapters)
        {
            lines.Add(FormatHardware("hardwareGpu", gpu.Name, gpu.DedicatedMemory / gib,
                gpu.UsedDedicatedMemory is { } used ? FormatHardware("hardwareGiB", used / gib) : T("hardwareUnknown"), gpu.SharedMemoryLimit / gib));
        }
        lines.Add(FormatHardware("hardwareAdmission", HardwareProfile.RequiredAvailableRam(model) / gib));
        if (!profile.CanLoad(model)) { lines.Add(T("hardwareLowRam")); }
        if (!_models.GetInstalledModels().Contains(ModelId, StringComparer.Ordinal) && !profile.CanInstall(model)) { lines.Add(T("hardwareLowDisk")); }
        _hardware.Text = string.Join("\n", lines);
    }

    /// <summary>Selects a smaller reviewed artifact only after the explicit native button; Save persists the choice.</summary>
    private Task SelectSmallerModelAsync()
    {
        int selected = Array.FindIndex(_catalogue, model => model.Size < _catalogue[Math.Max(0, _model.SelectedIndex)].Size);
        if (selected >= 0) { _model.SelectedIndex = selected; } return Task.CompletedTask;
    }
    /// <summary>Formats capacity labels with the independent interface culture.</summary>
    private string FormatHardware(string key, params object[] values) => string.Format(_ui.Culture, T(key), values);
    /// <summary>Checks real hardware labels and an explicit reversible smaller-model selection without loading weights or audio.</summary>
    internal async Task<HardwareProfile> VerifyHardwareForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Hardware UI diagnostics require isolated smoke mode."); }
        await RefreshHardwareAsync().ConfigureAwait(true);
        var profile = _profile ?? throw new InvalidOperationException("Hardware observation did not produce a snapshot.");
        if (profile.Processors <= 0 || profile.TotalRam <= 0 || _hardware.Text.Contains("ui.models.", StringComparison.Ordinal))
        { throw new InvalidOperationException("Hardware summary contains unavailable measurements or untranslated labels."); }
        int original = _model.SelectedIndex; long originalSize = _catalogue[Math.Max(0, original)].Size;
        int originalBackend = _backend.SelectedIndex;
        try
        {
            await SelectSmallerModelAsync().ConfigureAwait(true);
            _backend.SelectedIndex = 1;
            if (GpuLayersPreference != 0) { throw new InvalidOperationException("Explicit CPU selection was not retained."); }
            _backend.SelectedIndex = 2;
            if (GpuLayersPreference != 16) { throw new InvalidOperationException("Reviewed partial offload selection was not retained."); }
            if (_catalogue.Any(model => model.Size < originalSize) && _catalogue[_model.SelectedIndex].Size >= originalSize)
            { throw new InvalidOperationException("The explicit smaller-model action did not select a smaller reviewed artifact."); }
        }
        finally { _model.SelectedIndex = original; _backend.SelectedIndex = originalBackend; }
        return profile;
    }
    /// <summary>Dispatches native loading progress to the control's UI thread.</summary>
    private void OnLoadProgress(object? sender, float progress) => Dispatcher.Dispatch(() =>
        { if (_operation is not null) { _state.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("loadingProgress"), Math.Clamp(progress, 0, 1) * 100); } });
    /// <summary>Reads localized model-control text.</summary>
    private string T(string key) => _ui.Get("ui.models." + key);
}

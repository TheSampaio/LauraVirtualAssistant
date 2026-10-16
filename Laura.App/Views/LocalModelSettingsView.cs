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
    private readonly ModelArtifact[] _catalogue;
    private readonly Label _state = Theme.Label("", 12, Theme.Muted);
    private CancellationTokenSource? _operation;

    /// <summary>Creates isolated model actions. Args: models: Reviewed downloads; inference: Native lifecycle; settings: Current selection; ui: Central resources.</summary>
    public LocalModelSettingsView(ILocalModelManager models, ILocalInferenceEngine inference, ISettingsService settings, IUiLocalizer ui)
    {
        _models = models; _inference = inference; _ui = ui; _catalogue = models.Catalog.Where(model => !model.IsSpeechModel).ToArray();
        _model = Theme.Picker(_catalogue.Select(model => model.Name), _catalogue.First(model => model.Id == settings.Current.GenerativeAi.LocalModel).Name);
        _model.SelectedIndexChanged += (_, _) => Refresh();
        Content = Theme.Stack(Theme.Label(T("localTitle"), 22), _model, Theme.Label(T("localHelp"), 12, Theme.Muted), _state,
            Theme.ActionButton(T("install"), "upload", InstallAsync), Theme.ActionButton(T("load"), "play", () => RunAsync(token => _inference.LoadAsync(ModelId, token))),
            Theme.ActionButton(T("installSpeech"), "mic", () => RunAsync(token => _models.InstallAsync(SupportedModels.Speech, null, token))),
            Theme.ActionButton(T("unload"), "square", () => RunAsync(_inference.UnloadAsync)),
            Theme.ActionButton(T("cancel"), "x", () => { _operation?.Cancel(); _inference.Cancel(); return Task.CompletedTask; }));
        Loaded += (_, _) => _inference.LoadingProgress += OnLoadProgress;
        Unloaded += (_, _) => { _operation?.Cancel(); _inference.LoadingProgress -= OnLoadProgress; }; Refresh();
    }

    /// <summary>Gets the selected reviewed model ID; the existing settings Save action persists it.</summary>
    public string ModelId => _catalogue[Math.Max(0, _model.SelectedIndex)].Id;
    /// <summary>Restores the saved selection when settings reopen.</summary>
    public void Reload(string modelId) { _model.SelectedIndex = Array.FindIndex(_catalogue, model => model.Id == modelId); Refresh(); }

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
    private void Refresh() => _state.Text = (_models.GetInstalledModels().Contains(ModelId, StringComparer.Ordinal) ? T("installed") : T("missing"))
        + " · " + T("state." + _inference.Status) + " · " + T(File.Exists(_models.GetModelPath(SupportedModels.Speech.Id)) ? "speechInstalled" : "speechMissing");
    /// <summary>Dispatches native loading progress to the control's UI thread.</summary>
    private void OnLoadProgress(object? sender, float progress) => Dispatcher.Dispatch(() =>
        { if (_operation is not null) { _state.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("loadingProgress"), Math.Clamp(progress, 0, 1) * 100); } });
    /// <summary>Reads localized model-control text.</summary>
    private string T(string key) => _ui.Get("ui.models." + key);
}

using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Models;
using Laura.Platform.Windows.System;

namespace Laura.App.Views;

public sealed partial class SettingsForm
{
    private ILocalModelManager? _localModels;
    private ILocalInferenceEngine? _inference;
    private IModelCatalog? _ollamaModels;
    private ComboBox _providerCombo = null!;
    private ComboBox _downloadCombo = null!;
    private ComboBox _installedCombo = null!;
    private Label _modelInfo = null!;
    private Label _downloadStatus = null!;
    private ProgressBar _downloadProgress = null!;
    private CancellationTokenSource? _modelOperation;
    private readonly System.Text.StringBuilder _streamedResponse = new();
    private Label _streamPreview = null!;

    /// <summary>Binds native lifecycle operations from the composition root.</summary>
    public void BindInference(ILocalInferenceEngine inference)
    {
        _inference = inference;
        _inference.TokenReceived += OnInferenceToken;
    }

    /// <summary>Marshals streamed text into the chat preview without touching controls on the inference thread.</summary>
    private void OnInferenceToken(object? sender, string text) => _dispatcher.Post(() =>
    {
        if (IsDisposed || _streamPreview.IsDisposed) { return; }
        _streamedResponse.Append(text);
        _streamPreview.Text = _streamedResponse.ToString();
    });

    /// <summary>Binds the optional preexisting Ollama model catalog.</summary>
    public void BindOllamaCatalog(IModelCatalog models) => _ollamaModels = models;

    /// <summary>Opens integrated first-run model setup.</summary>
    public void ShowModelSetup() => SelectSection(Text_("ui.tab.models"));

    /// <summary>Builds model installation and lifecycle controls inside Laura's existing window.</summary>
    private Panel BuildModelsSection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(Text_("ui.tab.models"));
        AddRow(stack, SettingRowFactory.Heading(Text_("ui.models.intro")));
        AddRow(stack, SettingRowFactory.ComboRow(Text_("ui.models.catalog"), Text_("ui.models.network"), out _downloadCombo));
        if (_localModels is not null)
        {
            foreach (ModelArtifact model in _localModels.Catalog.Where(static model => !model.IsSpeechModel))
            {
                _downloadCombo.Items.Add(new ComboItem(model.Name, model.Id));
            }
        }
        _modelInfo = new Label { AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false,
            ForeColor = Palette.TextSecondary, Padding = new Padding(8) };
        AddRow(stack, _modelInfo);
        _downloadCombo.SelectedIndexChanged += (_, _) => RefreshModelDetails();
        if (_downloadCombo.Items.Count > 0) { _downloadCombo.SelectedIndex = 0; }
        _downloadStatus = new Label { AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false,
            ForeColor = Palette.TextPrimary, Padding = new Padding(8) };
        _downloadProgress = new ProgressBar { Dock = DockStyle.Top, Height = 20 };
        AddRow(stack, _downloadProgress);
        AddRow(stack, _downloadStatus);
        var install = new FlatButton { Text = Text_("ui.models.install") };
        install.Click += async (_, _) => await RunModelOperationAsync(async token =>
        {
            ModelArtifact? artifact = SelectedArtifact();
            if (artifact is null || _localModels is null) { return; }
            await _localModels.InstallAsync(artifact, new Progress<ModelDownloadProgress>(progress =>
            {
                if (IsDisposed || _downloadStatus.IsDisposed) { return; }
                _downloadProgress.Value = (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes, 0, 100);
                _downloadStatus.Text = progress.Verifying ? Text_("ui.models.verifying") :
                    $"{progress.BytesReceived / 1048576} / {progress.TotalBytes / 1048576} MiB";
            }), token).ConfigureAwait(true);
            RefreshInstalledModels();
        }).ConfigureAwait(true);
        AddRow(stack, install);
        var cancel = new FlatButton { Text = Text_("ui.actions.cancel") };
        cancel.Click += (_, _) => { _modelOperation?.Cancel(); _inference?.Cancel(); };
        AddRow(stack, cancel);
        AddRow(stack, SettingRowFactory.ComboRow(Text_("ui.models.installed"), string.Empty, out _installedCombo));
        var load = new FlatButton { Text = Text_("ui.models.load") };
        load.Click += async (_, _) => await RunModelOperationAsync(async token =>
        {
            string? id = (_installedCombo.SelectedItem as ComboItem)?.Value;
            if (id is null || _inference is null) { return; }
            await _inference.LoadAsync(id, token).ConfigureAwait(true);
            var current = _settingsService.Current;
            await _settingsService.UpdateAsync(current with { GenerativeAi = current.GenerativeAi with
                { Provider = "embedded", Model = id, Enabled = true } }, token).ConfigureAwait(true);
            _model.GenerativeAiModel = id;
            _model.GenerativeAiProvider = "embedded";
            _model.GenerativeAiEnabled = true;
            PopulateModelCombo();
            _aiToggle.Checked = true;
            _providerCombo.SelectedIndex = 0;
        }).ConfigureAwait(true);
        AddRow(stack, load);
        var unload = new FlatButton { Text = Text_("ui.models.unload") };
        unload.Click += async (_, _) => await RunModelOperationAsync(async token =>
        {
            if (_inference is not null) { await _inference.UnloadAsync(token).ConfigureAwait(true); }
            var current = _settingsService.Current;
            await _settingsService.UpdateAsync(current with { GenerativeAi = current.GenerativeAi with { Enabled = false } }, token).ConfigureAwait(true);
            _model.GenerativeAiEnabled = false;
            _aiToggle.Checked = false;
        }).ConfigureAwait(true);
        AddRow(stack, unload);
        var delete = new FlatButton { Text = Text_("ui.models.delete") };
        delete.Click += async (_, _) =>
        {
            string? id = (_installedCombo.SelectedItem as ComboItem)?.Value;
            if (id is null || MessageBox.Show(this, Text_("ui.models.deleteConfirm"), Text_("ui.tab.models"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { return; }
            await RunModelOperationAsync(async token =>
            {
                if (_inference is not null) { await _inference.UnloadAsync(token).ConfigureAwait(true); }
                _localModels?.Delete(id);
                var current = _settingsService.Current;
                if (current.GenerativeAi.Model == id)
                {
                    await _settingsService.UpdateAsync(current with { GenerativeAi = current.GenerativeAi with
                        { Model = string.Empty, Enabled = false } }, token).ConfigureAwait(true);
                    _model.GenerativeAiModel = string.Empty;
                    _model.GenerativeAiEnabled = false;
                    _aiToggle.Checked = false;
                }
                RefreshInstalledModels();
            }).ConfigureAwait(true);
        };
        AddRow(stack, delete);
        RefreshInstalledModels();
        return page;
    }

    /// <summary>Finds the reviewed artifact selected by the user.</summary>
    private ModelArtifact? SelectedArtifact() => _localModels?.Catalog.FirstOrDefault(
        model => model.Id == (_downloadCombo.SelectedItem as ComboItem)?.Value);

    /// <summary>Shows measured hardware and download/license requirements before installation.</summary>
    private void RefreshModelDetails()
    {
        ModelArtifact? artifact = SelectedArtifact();
        if (artifact is null || _localModels is null) { return; }
        try
        {
            HardwareProfile hardware = WindowsHardwareProbe.Detect(Path.GetDirectoryName(_localModels.GetModelPath(artifact.Id))!);
            _modelInfo.Text = _localizer.Get("ui.models.details", artifact.Size / 1048576, artifact.Quantization,
                artifact.ContextLength, hardware.Processors, hardware.TotalRam / 1073741824.0,
                hardware.AvailableRam / 1073741824.0, hardware.FreeDisk / 1073741824.0,
                artifact.RecommendedRam / 1073741824, artifact.License) + "\n" + artifact.LicenseUrl + "\n" +
                Text_(hardware.CanRun(artifact) ? "ui.models.compatible" : "ui.models.limited");
        }
        catch (IOException) { _modelInfo.Text = Text_("ui.models.hardwareUnavailable"); }
    }

    /// <summary>Refreshes installed model selectors without discarding the saved selection.</summary>
    private void RefreshInstalledModels()
    {
        _installedCombo.Items.Clear();
        foreach (string id in _localModels?.GetInstalledModels() ?? []) { _installedCombo.Items.Add(new ComboItem(id, id)); }
        if (_installedCombo.Items.Count > 0) { _installedCombo.SelectedIndex = 0; }
        PopulateModelCombo();
    }

    /// <summary>Runs one asynchronous model operation; failures keep setup usable for retry.</summary>
    private async Task RunModelOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_modelOperation is not null) { return; }
        using var cancellation = new CancellationTokenSource();
        _modelOperation = cancellation;
        _downloadStatus.Text = Text_("ui.models.working");
        try { await operation(cancellation.Token).ConfigureAwait(true); _downloadStatus.Text = Text_("ui.models.done"); }
        catch (OperationCanceledException) { _downloadStatus.Text = Text_("ui.models.cancelled"); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _downloadStatus.Text = _localizer.Get("ui.models.failed", DescribeFailure(exception));
        }
        finally
        {
            _modelOperation = null;
            if (_speechModelStatus is not null && !_speechModelStatus.IsDisposed) { _speechModelStatus.Text = _downloadStatus.Text; }
        }
    }
}

using System.Globalization;
using Laura.App.Controls;
using Laura.Core.Abstractions;
using Laura.Core.Engine;
using Laura.Core.Models;
using Microsoft.Extensions.Logging;

namespace Laura.App.Views;

public sealed partial class SettingsForm
{
    private ISpeechInput? _speechInput;
    private ComboBox _inputDeviceCombo = null!;
    private ComboBox _outputDeviceCombo = null!;
    private ToggleSwitch _muteMic = null!;
    private FlatButton _talkButton = null!;
    private Label _speechModelStatus = null!;
    private CancellationTokenSource? _recordingStop;
    private readonly CancellationTokenSource _voiceLifetime = new();

    /// <summary>Connects offline input without opening the microphone.</summary>
    public void BindSpeechInput(ISpeechInput speechInput)
    {
        _speechInput = speechInput;
        PopulateAudioDevices();
    }

    /// <summary>Adds half-duplex push-to-talk and device controls.</summary>
    private void AddVoiceInputControls(TableLayoutPanel stack)
    {
        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.voice.muteMic"), Text_("ui.voice.localInput"), out _muteMic));
        _muteMic.CheckedChanged += (_, _) => { if (_muteMic.Checked) { _recordingStop?.Cancel(); } };
        AddRow(stack, SettingRowFactory.ComboRow(Text_("ui.voice.microphone"), string.Empty, out _inputDeviceCombo));
        AddRow(stack, SettingRowFactory.ComboRow(Text_("ui.voice.output"), string.Empty, out _outputDeviceCombo));
        _talkButton = new FlatButton { Text = Text_("ui.voice.talk") };
        _talkButton.Click += async (_, _) => await ToggleVoiceInputAsync().ConfigureAwait(true);
        AddRow(stack, _talkButton);
        var installSpeech = new FlatButton { Text = Text_("ui.voice.installSpeech") };
        installSpeech.Click += async (_, _) => await RunModelOperationAsync(async token =>
        {
            if (_localModels is null) { return; }
            await _localModels.InstallAsync(SupportedModels.Speech, new Progress<ModelDownloadProgress>(progress =>
            {
                if (!IsDisposed) { _speechModelStatus.Text = $"{progress.BytesReceived / 1048576} / {progress.TotalBytes / 1048576} MiB"; }
            }), token).ConfigureAwait(true);
            ShowTransientStatus(Text_("ui.models.done"));
        }).ConfigureAwait(true);
        AddRow(stack, installSpeech);
        var cancelSpeech = new FlatButton { Text = Text_("ui.actions.cancel") };
        cancelSpeech.Click += (_, _) => _modelOperation?.Cancel();
        AddRow(stack, cancelSpeech);
        _speechModelStatus = new Label { AutoSize = true, UseMnemonic = false, ForeColor = Theming.Palette.TextSecondary };
        AddRow(stack, _speechModelStatus);
        AddRow(stack, SettingRowFactory.Heading(Text_("ui.voice.speechModelInfo")));
        AddRow(stack, SettingRowFactory.Heading(Text_("ui.voice.wakeUnavailable")));
        PopulateAudioDevices();
    }

    /// <summary>Enumerates devices; unavailable indices fall back to the default.</summary>
    private void PopulateAudioDevices()
    {
        if (_inputDeviceCombo is null) { return; }
        _inputDeviceCombo.Items.Clear();
        _outputDeviceCombo.Items.Clear();
        _inputDeviceCombo.Items.Add(new ComboItem(Text_("ui.voice.defaultDevice"), "-1"));
        _outputDeviceCombo.Items.Add(new ComboItem(Text_("ui.voice.defaultDevice"), "-1"));
        try
        {
            foreach (var device in _speechInput?.InputDevices ?? [])
            {
                _inputDeviceCombo.Items.Add(new ComboItem(device.Name, device.Id.ToString(CultureInfo.InvariantCulture)));
            }
            foreach (var device in _speechInput?.OutputDevices ?? [])
            {
                _outputDeviceCombo.Items.Add(new ComboItem(device.Name, device.Id.ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch (Exception exception) { _logger.LogWarning(exception, "Audio enumeration failed."); }
        _inputDeviceCombo.SelectedIndex = Math.Clamp(_model.InputDevice + 1, 0, _inputDeviceCombo.Items.Count - 1);
        _outputDeviceCombo.SelectedIndex = Math.Clamp(_model.OutputDevice + 1, 0, _outputDeviceCombo.Items.Count - 1);
        _muteMic.Checked = _model.MicrophoneMuted;
    }

    /// <summary>Starts capture only after explicit unmute and while the assistant is idle.</summary>
    private async Task ToggleVoiceInputAsync()
    {
        if (_recordingStop is not null) { _recordingStop.Cancel(); return; }
        if (_speechInput is null || _muteMic.Checked) { ShowTransientStatus(Text_("ui.voice.muted")); return; }
        if (_engine.State != AssistantState.Ready) { ShowTransientStatus(Text_("ui.voice.wait")); return; }
        CommitControlsToModel();
        using var stop = new CancellationTokenSource();
        _recordingStop = stop;
        _talkButton.Text = Text_("ui.voice.finish");
        _synthesizer.CancelSpeech();
        try
        {
            string? text = await _speechInput.RecognizeAsync(_model.Culture, _model.InputDevice,
                stop.Token, _voiceLifetime.Token).ConfigureAwait(true);
            if (!_muteMic.Checked && !string.IsNullOrWhiteSpace(text))
            {
                // Recognition is reviewed before it can run a computer action.
                _chatInputBox.Text = text;
                SelectSection(Text_("ui.tab.chat"));
                ShowTransientStatus(Text_("ui.voice.review"));
            }
            else { ShowTransientStatus(Text_("ui.voice.noSpeech")); }
        }
        catch (OperationCanceledException) { /* Closing discards captured audio. */ }
        catch (Exception exception) { ShowTransientStatus(_localizer.Get("ui.voice.failed", DescribeFailure(exception))); }
        finally
        {
            _recordingStop = null;
            if (!_talkButton.IsDisposed) { _talkButton.Text = Text_("ui.voice.talk"); }
        }
    }
}

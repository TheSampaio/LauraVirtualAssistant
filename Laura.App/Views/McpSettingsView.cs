using System.Text.Json;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Mcp;

namespace Laura.App.Views;

/// <summary>Native explicit server configuration and lifecycle. Saving never installs or connects a server, and model tools cannot edit these controls.</summary>
public sealed class McpSettingsView : ContentView
{
    private readonly McpServerStore _store;
    private readonly McpRuntime _runtime;
    private readonly IUiLocalizer _ui;
    private readonly Picker _servers = Theme.Picker([], "");
    private readonly Entry _id = Theme.Entry(), _name = Theme.Entry(), _command = Theme.Entry(), _directory = Theme.Entry();
    private readonly Editor _arguments = new() { Text = "[]", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 80, BackgroundColor = Theme.Field, TextColor = Theme.Text, FontFamily = "Consolas" };
    private readonly Label _status = Theme.Label("", 12, Theme.Muted);
    private readonly VerticalStackLayout _tools = new() { Spacing = 8 };
    private IReadOnlyList<McpServerDefinition> _definitions = [];
    private string? _selected;
    /// <summary>Builds localized controls using the existing settings tokens, SVG actions and native approval center.</summary>
    public McpSettingsView(McpServerStore store, McpRuntime runtime, IUiLocalizer ui)
    {
        _store = store; _runtime = runtime; _ui = ui;
        _servers.SelectedIndexChanged += (_, _) => Select(_servers.SelectedIndex);
        var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        Add("new", "plus", () => { Select(-1); return Task.CompletedTask; });
        Add("save", "save", SaveAsync); Add("remove", "trash", RemoveAsync);
        Add("connect", "link", async () => { if (_selected is null) { throw new InvalidOperationException(); } await _runtime.ConnectAsync(_selected).ConfigureAwait(true); RefreshState(); });
        Add("disconnect", "x", async () => { if (_selected is not null) { await _runtime.DisconnectAsync(_selected).ConfigureAwait(true); } RefreshState(); });
        Add("refresh", "refresh_cw", async () =>
        {
            if (_selected is null) { throw new InvalidOperationException(); }
            IReadOnlyList<McpTool> tools = await _runtime.DiscoverAsync(_selected).ConfigureAwait(true); _tools.Clear();
            foreach (McpTool tool in tools) { _tools.Add(Theme.Card(Theme.Stack(Theme.Label(tool.Name, 13), Theme.Label(tool.Description, 12, Theme.Muted)), 12)); }
            RefreshState();
        });
        Content = Theme.Stack(Theme.Label(T("title"), 22), Theme.Label(T("help"), 13, Theme.Muted), _servers,
            Field("id", _id), Field("name", _name), Field("command", _command),
            Theme.ActionButton(T("browse"), "folder", async () =>
            {
                FileResult? file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("command") }).ConfigureAwait(true);
                if (file is not null) { _command.Text = file.FullPath; if (string.IsNullOrWhiteSpace(_directory.Text)) { _directory.Text = Path.GetDirectoryName(file.FullPath); } }
            }), Field("directory", _directory), Field("arguments", _arguments), Theme.Label(T("argumentsHelp"), 12, Theme.Muted), buttons, _status, _tools);
        Loaded += (_, _) => Reload();
        void Add(string key, string icon, Func<Task> action)
        { Button button = Theme.ActionButton(T(key), icon, () => GuardAsync(action)); button.Margin = new Thickness(0, 0, 8, 8); buttons.Add(button); }
    }
    private string T(string key) => _ui.Get("ui.mcp." + key);
    private View Field(string key, View control) { SemanticProperties.SetDescription(control, T(key)); return Theme.Stack(Theme.Label(T(key)), control); }
    private void Reload()
    {
        try { _definitions = _store.Read(); _servers.ItemsSource = _definitions.Select(item => item.Name + " · " + item.Id).ToArray(); Select(-1); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { _status.Text = T("configurationError"); }
    }
    private void Select(int index)
    {
        McpServerDefinition? definition = index >= 0 && index < _definitions.Count ? _definitions[index] : null;
        _selected = definition?.Id; _id.IsEnabled = definition is null; _id.Text = definition?.Id ?? ""; _name.Text = definition?.Name ?? "";
        _command.Text = definition?.Executable ?? ""; _directory.Text = definition?.Directory ?? ""; _arguments.Text = JsonSerializer.Serialize(definition?.Arguments ?? []); _tools.Clear(); RefreshState();
    }
    private void RefreshState()
    {
        McpServerState? state = _runtime.States().SingleOrDefault(state => state.Id == _selected);
        _status.Text = state is null ? T("not_connected") : T(state.State) + " · " + state.ToolCount.ToString(_ui.Culture) + " " + T("tools") + (state.Protocol.Length > 0 ? " · MCP " + state.Protocol : "") + (state.Failure.Length > 0 ? " · " + T("failed") : "");
    }
    private async Task SaveAsync()
    {
        if ((_arguments.Text ?? "").Length > 32000) { throw new ArgumentException(); }
        string[] arguments = JsonSerializer.Deserialize<string[]>(_arguments.Text ?? "[]") ?? throw new ArgumentException();
        var definition = new McpServerDefinition(_id.Text?.Trim() ?? "", _name.Text?.Trim() ?? "", _command.Text?.Trim() ?? "", arguments, _directory.Text?.Trim() ?? "");
        if (_selected is null && _definitions.Any(item => item.Id == definition.Id)) { throw new ArgumentException(); }
        await _runtime.ConfigureAsync(_definitions.Where(item => item.Id != _selected).Append(definition).ToArray()).ConfigureAwait(true); Reload(); _servers.SelectedIndex = _definitions.ToList().FindIndex(item => item.Id == definition.Id); _status.Text = T("saved");
    }
    private async Task RemoveAsync()
    {
        if (_selected is null || !await Laura.App.Platforms.Windows.WindowsDialogs.AlertAsync(T("remove"), T("removeHelp"), T("remove"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { return; }
        await _runtime.ConfigureAsync(_definitions.Where(item => item.Id != _selected).ToArray()).ConfigureAwait(true); Reload();
    }
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (OperationCanceledException) { _status.Text = T("cancelled"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { _status.Text = T("failed"); }
    }
}

using Laura.App.Infrastructure;
using Laura.App.Platforms.Windows;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;

namespace Laura.App.Views;

/// <summary>Native private task workspace with observed progress, scoped cancellation and explicit checkpoint resumption.</summary>
public sealed class TasksPage : ContentPage
{
    private readonly CognitiveTasks _tasks;
    private readonly IAssistantEngine _engine;
    private readonly ConversationProviderRouter _router;
    private readonly IUiLocalizer _ui;
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None };
    private readonly Label _status = Theme.Label("", 12, Theme.Muted);
    private int _refreshRevision;
    private bool _visible;

    /// <summary>Creates the localized task workspace. Args: tasks: Durable coordinator; engine: Existing command queue; router: Actual provider; ui: Interface resources.</summary>
    public TasksPage(CognitiveTasks tasks, IAssistantEngine engine, ConversationProviderRouter router, IUiLocalizer ui)
    {
        _tasks = tasks; _engine = engine; _router = router; _ui = ui; Title = T("title"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        var heading = Theme.PageHeading(Title, T("subtitle"), "activity", ui.Get("ui.navigation.backToLaura"), () => NavigationMotion.PopAsync(Navigation));
        var root = new Grid { Padding = 24, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)], RowSpacing = 16 };
        root.Add(heading); root.Add(_status, 0, 1); root.Add(_list, 0, 2); Content = root;
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var cell = new ContentView { Padding = new Thickness(0, 0, 0, 12) };
            cell.BindingContextChanged += (_, _) => { if (cell.BindingContext is CognitiveTaskCheckpoint task) { cell.Content = Card(task); } };
            return cell;
        });
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    { base.OnAppearing(); _visible = true; _tasks.Changed += Changed; _ = RefreshAsync(); }
    /// <inheritdoc />
    protected override void OnDisappearing()
    { _visible = false; _refreshRevision++; _tasks.Changed -= Changed; base.OnDisappearing(); }

    /// <summary>Builds one virtualized task row from durable native metadata, without rendering private model reasoning.</summary>
    private View Card(CognitiveTaskCheckpoint task)
    {
        string state = task.HasUncertainEffect ? T("uncertain") : T("state." + task.State);
        var details = Theme.Stack(Theme.Label(task.Objective, 16), Theme.Label(state + " · " + string.Format(System.Globalization.CultureInfo.CurrentCulture, T("calls"), task.Calls, task.MaximumCalls), 12, Theme.Muted),
            Theme.Label(task.Updated.ToLocalTime().ToString("g", _ui.Culture), 11, Theme.Muted));
        foreach (var step in task.Steps) { details.Add(Theme.Label(step.Number + ". " + step.Tool + " · " + T("state." + step.State), 12)); }
        if (task.Operations.Count > 0)
        { var latest = task.Operations[^1]; details.Add(Theme.Label(latest.Tool + " · " + T("state." + latest.Status), 12, Theme.Muted)); }
        var resume = Theme.ActionButton(T("resume"), "play", () => ResumeAsync(task));
        resume.IsEnabled = (task.State is "paused" or "working" or "operations_completed") && !task.HasUncertainEffect
            && task.Calls < task.MaximumCalls && task.ElapsedMilliseconds < task.MaximumMilliseconds;
        var actions = new HorizontalStackLayout { Spacing = 8 };
        actions.Add(resume); actions.Add(Theme.ActionButton(T("cancel"), "square", () => { _tasks.Cancel(task.Id); return Task.CompletedTask; }));
        actions.Add(Theme.ActionButton(T("remove"), "trash", () => RemoveAsync(task.Id))); details.Add(actions);
        return Theme.Card(details, 16);
    }

    /// <summary>Queues explicit recovery through the actual selected provider and existing speech/history pipeline.</summary>
    private async Task ResumeAsync(CognitiveTaskCheckpoint task)
    {
        if (_router.ActiveProvider == "local") { _status.Text = T("localLimited"); return; }
        _status.Text = T("resuming");
        try { await _engine.ResumeTaskAsync(task.Id).ConfigureAwait(true); await RefreshAsync().ConfigureAwait(true); }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.Security.Cryptography.CryptographicException)
        { _status.Text = T("resumeFailed") + " · " + error.GetType().Name; }
    }

    /// <summary>Deletes only the selected private checkpoint after native confirmation, preserving Brain knowledge and files.</summary>
    private async Task RemoveAsync(Guid id)
    {
        if (!await WindowsConfirmation.AskAsync(T("removeConfirm"), T("remove"), _ui.Get("ui.actions.cancel"), CancellationToken.None).ConfigureAwait(true)) { return; }
        try { await _tasks.DeleteAsync(id).ConfigureAwait(true); await RefreshAsync().ConfigureAwait(true); }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _status.Text = T("removeFailed") + " · " + error.GetType().Name; }
    }

    /// <summary>Reads protected state on a worker and applies only the newest visible-page result.</summary>
    internal async Task RefreshAsync()
    {
        int revision = ++_refreshRevision;
        try
        {
            var tasks = await Task.Run(() => _tasks.ListAsync()).ConfigureAwait(true);
            if (revision != _refreshRevision || !_visible) { return; }
            _list.ItemsSource = tasks; _status.Text = tasks.Count == 0 ? T("empty") : T("help");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { if (_visible) { _status.Text = T("loadFailed") + " · " + error.GetType().Name; } }
    }

    /// <summary>Checks the real virtualized viewport and translated labels in the explicitly isolated UI harness.</summary>
    internal void VerifyForSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated UI diagnostics only."); }
        if (_list.Height < 120 || _list.Width < 280 || Title.StartsWith("ui.", StringComparison.Ordinal) || _list.ItemsSource is not IReadOnlyList<CognitiveTaskCheckpoint> { Count: > 0 })
        { throw new InvalidOperationException("Task workspace lost its usable viewport, localized title or retained task."); }
    }

    /// <summary>Coalesces metadata changes on the UI dispatcher without waiting on the active task's execution lane.</summary>
    private void Changed(object? sender, EventArgs args) => Dispatcher.Dispatch(async () =>
    { int revision = ++_refreshRevision; await Task.Delay(150).ConfigureAwait(true); if (_visible && revision == _refreshRevision) { await RefreshAsync().ConfigureAwait(true); } });
    /// <summary>Reads centralized interface text independently from conversation language.</summary>
    private string T(string key) => _ui.Get("ui.tasks." + key);
}

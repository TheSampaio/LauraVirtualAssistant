using Laura.App.Infrastructure;
using Laura.App.Platforms.Windows;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Brain;
using Laura.Core.Execution;

namespace Laura.App.Views;

/// <summary>Native private task workspace with observed progress, scoped cancellation and explicit checkpoint resumption.</summary>
public sealed class TasksPage : ContentPage
{
    private readonly CognitiveTasks _tasks;
    private readonly IAssistantEngine _engine;
    private readonly ConversationProviderRouter _router;
    private readonly IUiLocalizer _ui;
    private readonly RepositoryStudy _studies;
    private readonly RepositoryAnalysis _analysis;
    private readonly CommandJobs _commandJobs;
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None };
    private readonly Label _status = Theme.Label("", 12, Theme.Muted);
    private int _refreshRevision;
    private bool _visible;

    /// <summary>Creates the localized task workspace. Args: tasks: Durable coordinator; engine: Existing command queue; router: Actual provider; ui: Interface resources.</summary>
    public TasksPage(CognitiveTasks tasks, IAssistantEngine engine, ConversationProviderRouter router, IUiLocalizer ui, RepositoryStudy studies, CommandJobs commandJobs, RepositoryAnalysis analysis)
    {
        _tasks = tasks; _engine = engine; _router = router; _ui = ui; _studies = studies; _analysis = analysis; _commandJobs = commandJobs; Title = T("title"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
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
    { base.OnAppearing(); _visible = true; _tasks.Changed += Changed; _commandJobs.Changed += Changed; _ = RefreshAsync(); }
    /// <inheritdoc />
    protected override void OnDisappearing()
    { _visible = false; _refreshRevision++; _tasks.Changed -= Changed; _commandJobs.Changed -= Changed; base.OnDisappearing(); }

    /// <summary>Builds one virtualized task row from durable native metadata, without rendering private model reasoning.</summary>
    private View Card(CognitiveTaskCheckpoint task)
    {
        string state = task.HasUncertainEffect ? T("uncertain") : T("state." + task.State);
        var details = Theme.Stack(Theme.Label(task.Objective, 16), Theme.Label(state + " · " + string.Format(System.Globalization.CultureInfo.CurrentCulture, T("calls"), task.Calls, task.MaximumCalls), 12, Theme.Muted),
            Theme.Label(task.Updated.ToLocalTime().ToString("g", _ui.Culture), 11, Theme.Muted));
        foreach (var step in task.Steps) { details.Add(Theme.Label(step.Number + ". " + step.Tool + " · " + T("state." + step.State), 12)); }
        if (task.Rounds is { } rounds)
        {
            details.Add(Theme.Label(string.Format(_ui.Culture, T("cycles"), rounds.Cycles, rounds.MaximumCycles, rounds.ProviderCalls, rounds.MaximumProviderCalls), 12, Theme.Muted));
            details.Add(Theme.Label(string.Format(_ui.Culture, T("provider"), rounds.Provider.Length > 0 ? rounds.Provider : T("unknown"), rounds.Model.Length > 0 ? rounds.Model : T("unknown")), 12, Theme.Muted));
            details.Add(Theme.Label(string.Format(_ui.Culture, T("activeBudget"), rounds.ActiveMilliseconds / 1000, rounds.MaximumActiveMilliseconds / 1000, rounds.Recoveries, rounds.MaximumRecoveries), 12, Theme.Muted));
            if (rounds.Paused) { details.Add(Theme.Label(T("ownerPaused"), 12)); }
            if (rounds.Canceled) { details.Add(Theme.Label(T("ownerCanceled"), 12)); }
            if (rounds.NeedsContinuation && !rounds.CanContinue) { details.Add(Theme.Label(T("budgetPaused"), 12)); }
        }
        if (task.Operations.Count > 0)
        { var latest = task.Operations[^1]; details.Add(Theme.Label(latest.Tool + " · " + T("state." + latest.Status), 12, Theme.Muted)); }
        var resume = Theme.ActionButton(T("resume"), "play", () => ResumeAsync(task));
        resume.IsEnabled = (task.State is "paused" or "working" or "operations_completed" || task.State == "blocked" && task.Blocker is "budget" or "cancelled" or "repetition") && !task.HasUncertainEffect
            && task.Calls < 256 && task.ElapsedMilliseconds < 3600000 && task.Rounds?.Outcome != "running";
        var actions = new HorizontalStackLayout { Spacing = 8 };
        var stop = Theme.ActionButton(T("cancel"), "square", () => _tasks.PauseAsync(task.Id, cancel: true)); stop.AutomationId = "StopTask" + task.Id.ToString("N");
        actions.Add(resume); actions.Add(Theme.ActionButton(T("pause"), "pause", () => _tasks.PauseAsync(task.Id))); actions.Add(stop);
        actions.Add(Theme.ActionButton(T("remove"), "trash", () => RemoveAsync(task.Id))); details.Add(actions);
        foreach (Guid study in task.RepositoryStudies ?? [])
        {
            var coverage = Theme.Label("", 12, Theme.Muted);
            details.Add(Theme.ActionButton(T("study"), "book_open", () => ShowStudyAsync(study, coverage))); details.Add(coverage);
        }
        var jobs = _commandJobs.List(task.Id).ToDictionary(job => job.Id);
        foreach (Guid id in (task.CommandJobs ?? []).Concat(jobs.Keys).Distinct())
        {
            if (!jobs.TryGetValue(id, out var job)) { details.Add(Theme.Label(T("jobUnavailable"), 12, Theme.Muted)); continue; }
            string stateKey = job.Status switch
            {
                "awaiting_approval" => "jobWaiting", "running" => "jobRunning", "exited" => job.Succeeded ? "jobSucceeded" : "jobFailed",
                "cancelled" => "state.cancelled", "timeout" => "jobTimeout", "termination_failed" => "jobCleanupFailed", "output_incomplete" => "jobOutputIncomplete",
                "blocked" => "state.blocked", _ => "state.failed"
            };
            details.Add(Theme.Label(string.Format(_ui.Culture, T("jobDetails"), T(stateKey), job.ProcessId, job.Duration.TotalSeconds), 12, Theme.Muted));
            if (job.ExitCode is { } exitCode) { details.Add(Theme.Label(string.Format(_ui.Culture, T("jobExitCode"), exitCode), 12, Theme.Muted)); }
            if (job.Truncated) { details.Add(Theme.Label(T("jobOutputTruncated"), 12, Theme.Muted)); }
            var output = Theme.Label("", 12);
            var outputViewport = new ScrollView { Content = output, HeightRequest = 180, IsVisible = false };
            long cursor = 0;
            var read = Theme.ActionButton(T("jobOutput"), "file_text", () =>
            {
                CommandJobSnapshot page;
                try { page = _commandJobs.ReadForNativeUser(task.Id, id, cursor); }
                catch (InvalidOperationException) { _status.Text = T("jobUnavailable"); return Task.CompletedTask; }
                cursor = page.NextSequence;
                outputViewport.IsVisible = true;
                output.Text = (page.OutputLost ? T("jobOutputLost") + "\n" : "")
                    + (page.Output.Count == 0 ? T("jobNoOutput") : T("jobStdout") + "\n" + string.Concat(page.Output.Where(chunk => !chunk.StandardError).Select(chunk => chunk.Text))
                        + "\n" + T("jobStderr") + "\n" + string.Concat(page.Output.Where(chunk => chunk.StandardError).Select(chunk => chunk.Text)));
                return Task.CompletedTask;
            }); read.AutomationId = "CommandOutput" + id.ToString("N"); details.Add(read); details.Add(outputViewport);
        }
        return Theme.Card(details, 16);
    }

    /// <summary>Shows actual durable repository coverage without waiting on the active model or reading source files.</summary>
    private async Task ShowStudyAsync(Guid id, Label coverage)
    {
        try
        {
            var progress = await Task.Run(() => _studies.ReadPrivateProgressAsync(id)).ConfigureAwait(true);
            var learning = await Task.Run(() => _analysis.StatusAsync(id)).ConfigureAwait(true);
            coverage.Text = string.Format(_ui.Culture, T("studyCoverage"), progress.AcquiredFiles, progress.InventoryFiles, progress.PendingFiles, progress.ExcludedFiles)
                + (progress.PendingInventoryDirectories > 0 ? " · " + string.Format(_ui.Culture, T("studyDirectories"), progress.PendingInventoryDirectories) : "")
                + (progress.InventoryTruncated ? " · " + T("studyTruncated") : "")
                + "\n" + string.Format(_ui.Culture, T("learningStages"), learning.Indexed, learning.AnalyzedModules.Count, learning.AnalyzedModules.Count(module => module.CurrentEvidence))
                + (learning.AnalyzedModules.Count == 0 ? "" : "\n" + string.Join(" · ", learning.AnalyzedModules.Select(module => module.Module)));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        { coverage.Text = T("loadFailed") + " · " + error.GetType().Name; }
    }

    /// <summary>Queues explicit recovery through the actual selected provider and existing speech/history pipeline.</summary>
    private async Task ResumeAsync(CognitiveTaskCheckpoint task)
    {
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

    /// <summary>Invokes the actual realized native output/stop button in isolated diagnostics. Args: taskId: Fixture task; jobId: Fixture job; stop: Whether to stop instead of read.</summary>
    internal async Task InvokeCommandActionForSmokeAsync(Guid taskId, Guid jobId, bool stop)
    {
        if (!Environment.GetCommandLineArgs().Contains("--command-jobs-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Command UI diagnostics require their isolated option."); }
        var items = ((IEnumerable<CognitiveTaskCheckpoint>)_list.ItemsSource).ToArray(); int index = Array.FindIndex(items, item => item.Id == taskId);
        if (index < 0) { throw new InvalidOperationException("Command task row is unavailable."); }
        _list.ScrollTo(index, position: ScrollToPosition.Start, animate: false); await Task.Delay(150).ConfigureAwait(true);
        var list = _list.Handler!.PlatformView as Microsoft.UI.Xaml.Controls.ListViewBase ?? throw new InvalidOperationException("Tasks require a native list.");
        var row = list.ContainerFromIndex(index) ?? throw new InvalidOperationException("Command task row was not realized.");
        string target = (stop ? "StopTask" + taskId.ToString("N") : "CommandOutput" + jobId.ToString("N"));
        var button = Find(row).OfType<Microsoft.UI.Xaml.Controls.Button>().FirstOrDefault(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) == target)
            ?? throw new InvalidOperationException("Command task action was not realized.");
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Task.Delay(150).ConfigureAwait(true);
        if (!stop && !Find(row).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(text => text.Text.Contains("synthetic background tick", StringComparison.Ordinal)))
        { throw new InvalidOperationException("Native output action did not render the observed fixture stream."); }
    }

    /// <summary>Enumerates the realized diagnostic row without inspecting any other application's UI.</summary>
    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Find(Microsoft.UI.Xaml.DependencyObject parent)
    {
        yield return parent;
        for (int child = 0; child < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); child++)
        { foreach (var descendant in Find(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, child))) { yield return descendant; } }
    }

    /// <summary>Coalesces metadata changes on the UI dispatcher without waiting on the active task's execution lane.</summary>
    private void Changed(object? sender, EventArgs args) => Dispatcher.Dispatch(async () =>
    { int revision = ++_refreshRevision; await Task.Delay(150).ConfigureAwait(true); if (_visible && revision == _refreshRevision) { await RefreshAsync().ConfigureAwait(true); } });
    /// <summary>Reads centralized interface text independently from conversation language.</summary>
    private string T(string key) => _ui.Get("ui.tasks." + key);
}

using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.App.Theming;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Laura.App.Views;

/// <summary>Displays bounded, payload-free native events with filtering, details and explicit local export.</summary>
public sealed class LogsPage : ContentPage
{
    private static readonly JsonSerializerOptions EventJson = new() { WriteIndented = true };
    private readonly RuntimeJournal _journal;
    private readonly IUiLocalizer _ui;
    private readonly Entry _search = Theme.Entry();
    private readonly Picker _filter, _detail;
    private readonly CollectionView _events = new() { SelectionMode = SelectionMode.Single };
    private readonly Editor _details = new() { IsReadOnly = true, TextColor = Theme.Text, BackgroundColor = Theme.Field, HeightRequest = 145 };
    private readonly Label _status = Theme.Label("", 12, Theme.Muted);
    private readonly IDispatcherTimer _timer;
    private IReadOnlyList<RuntimeLogEvent> _snapshot = [];
    private RuntimeLogEvent? _selected;
    private bool _refreshing;

    /// <summary>Creates the localized activity workspace. Args: journal: Existing asynchronous journal; settings: Detail preference; ui: Interface localization.</summary>
    public LogsPage(RuntimeJournal journal, ISettingsService settings, IUiLocalizer ui)
    {
        _journal = journal; _ui = ui;
        Title = T("title"); BackgroundColor = Theme.Background;
        NavigationPage.SetHasNavigationBar(this, false);
        _search.Placeholder = T("search");
        _filter = Theme.Picker([T("all"), .. Enum.GetValues<LogLevel>().Select(level => T("level." + level))], T("all"));
        _detail = Theme.Picker(Enum.GetValues<LogLevel>().Select(level => T("level." + level)), T("level." + settings.Current.DiagnosticLevel));
        _search.TextChanged += (_, _) => ApplyFilter();
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _detail.SelectedIndexChanged += async (_, _) =>
        {
            if (_detail.SelectedIndex < 0) { return; }
            await settings.UpdateAsync(settings.Current with { DiagnosticLevel = (LogLevel)_detail.SelectedIndex }).ConfigureAwait(true);
        };
        _events.ItemTemplate = new DataTemplate(() =>
        {
            var title = Theme.Label("", 13); title.SetBinding(Label.TextProperty, nameof(RuntimeLogEvent.Operation));
            var state = Theme.Label("", 11, Theme.Muted);
            state.SetBinding(Label.TextProperty, new Binding(nameof(RuntimeLogEvent.Timestamp), stringFormat: "{0:HH:mm:ss.fff}"));
            var outcome = Theme.Label("", 12, Theme.Muted); outcome.SetBinding(Label.TextProperty, nameof(RuntimeLogEvent.Outcome));
            var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Padding = 10 };
            row.Add(Theme.Stack(title, state)); row.Add(outcome, 1);
            return row;
        });
        _events.SelectionChanged += (_, args) =>
        {
            _selected = args.CurrentSelection.OfType<RuntimeLogEvent>().FirstOrDefault();
            _details.Text = _selected is null ? "" : JsonSerializer.Serialize(_selected, EventJson);
        };
        var controls = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        controls.Add(_search); controls.Add(_filter, 1);
        var actions = new HorizontalStackLayout { Spacing = 12 };
        actions.Add(Theme.ActionButton(T("refresh"), "refresh_cw", RefreshAsync));
        actions.Add(Theme.ActionButton(T("copy"), "copy", CopyAsync));
        actions.Add(Theme.ActionButton(T("export"), "download", ExportAsync));
        var body = new Grid { Padding = Theme.PageInset, RowSpacing = 12, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        body.Add(Theme.PageHeading(Title, T("subtitle"), "activity", ui.Get("ui.navigation.backToLaura"), () => NavigationMotion.PopAsync(Navigation)));
        body.Add(controls, 0, 1); body.Add(Theme.Stack(Theme.Label(T("detail"), 12), _detail), 0, 2);
        body.Add(Theme.Stack(actions, _status), 0, 3); body.Add(_events, 0, 4); body.Add(_details, 0, 5);
        Content = body;
        _timer = Dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnAppearing() { base.OnAppearing(); _timer.Start(); _ = RefreshAsync(); }
    /// <inheritdoc />
    protected override void OnDisappearing() { _timer.Stop(); base.OnDisappearing(); }
    /// <summary>Verifies actual native filtering and stable selected details using isolated metadata fixtures.</summary>
    internal async Task VerifyForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Log verification requires isolated UI smoke mode."); }
        while (_refreshing) { await Task.Delay(20).ConfigureAwait(true); }
        await RefreshAsync().ConfigureAwait(true);
        _search.Text = "process";
        var rows = ((IEnumerable<RuntimeLogEvent>?)_events.ItemsSource)?.ToArray() ?? [];
        if (rows.Length == 0 || rows.Any(row => !row.Operation.Contains("process", StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidOperationException("Native log filtering failed."); }
        _events.SelectedItem = rows[0];
        await RefreshAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(_details.Text)) { throw new InvalidOperationException("Log refresh discarded selected event details."); }
        _search.Text = "synthetic-no-matching-operation";
        if (((IEnumerable<RuntimeLogEvent>?)_events.ItemsSource)?.Any() == true) { throw new InvalidOperationException("Log search returned unrelated events."); }
        _search.Text = "process";
        _events.SelectedItem = ((IEnumerable<RuntimeLogEvent>?)_events.ItemsSource)?.FirstOrDefault();
    }
    /// <summary>Reads a consistent retained snapshot without allowing overlapping refreshes.</summary>
    private async Task RefreshAsync()
    {
        if (_refreshing) { return; } _refreshing = true;
        try
        {
            var snapshot = await _journal.SnapshotAsync().ConfigureAwait(true);
            _snapshot = snapshot.Events;
            _status.Text = string.Format(_ui.Culture, T("counts"), _snapshot.Count, snapshot.DroppedEvents, snapshot.WriteFailures);
            ApplyFilter();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { _status.Text = T("unavailable"); }
        finally { _refreshing = false; }
    }
    /// <summary>Filters symbolic metadata and identifiers while bounding the native list to the newest 500 matching events.</summary>
    private void ApplyFilter()
    {
        string search = _search.Text?.Trim() ?? "";
        RuntimeLogEvent? selected = _selected;
        var rows = _snapshot.Where(row => (_filter.SelectedIndex <= 0 || (int)row.Level == _filter.SelectedIndex - 1)
            && (search.Length == 0 || row.Operation.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Outcome.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.InteractionId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) || row.TaskId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.OperationId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Reverse().Take(500).ToArray();
        _events.ItemsSource = rows;
        if (selected is not null)
        { _events.SelectedItem = rows.FirstOrDefault(row => row.RunId == selected.RunId && row.Timestamp == selected.Timestamp && row.Operation == selected.Operation); }
    }
    /// <summary>Copies only the selected sanitized event on an explicit native gesture.</summary>
    private Task CopyAsync() => _selected is null ? Task.CompletedTask : Clipboard.Default.SetTextAsync(_details.Text);
    /// <summary>Exports the retained diagnostic window to an explicitly chosen local destination.</summary>
    private async Task ExportAsync()
    {
        if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
        { await WindowsDocumentExport.DiagnosticsAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), _journal).ConfigureAwait(true); }
    }
    /// <summary>Looks up one centralized interface label.</summary>
    private string T(string key) => _ui.Get("ui.logs." + key);
}

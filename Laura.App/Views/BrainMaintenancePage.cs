using Laura.App.Infrastructure;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Brain;

namespace Laura.App.Views;

/// <summary>Bounded, local review surface for incremental findings. Native confirmation precedes every reversible consolidation edit.</summary>
public sealed class BrainMaintenancePage : ContentPage, IDisposable
{
    private readonly BrainService _brain;
    private readonly BrainMaintenance _maintenance;
    private readonly ISettingsService _settings;
    private readonly IUiLocalizer _ui;
    private readonly Func<string, Task> _open;
    private readonly VerticalStackLayout _findings = new() { Spacing = 12 };
    private readonly Label _summary = Theme.Label("", 12, Theme.Muted);
    private readonly Entry _query = Theme.Entry();
    private readonly Switch _automatic = new() { OnColor = Theme.Accent };
    private bool _busy, _visible;
    private CancellationTokenSource? _viewLifetime;
    /// <summary>Creates a native workspace sharing Brain styles, navigation and the actual maintenance service.</summary>
    public BrainMaintenancePage(BrainService brain, BrainMaintenance maintenance, ISettingsService settings, IUiLocalizer ui, Func<string, Task> open)
    {
        _brain = brain; _maintenance = maintenance; _settings = settings; _ui = ui; _open = open;
        Title = T("title"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        _query.Placeholder = T("search"); _query.TextChanged += (_, _) => Render();
        _automatic.IsToggled = settings.Current.Brain.MaintenanceEnabled;
        SemanticProperties.SetDescription(_automatic, T("automatic"));
        _automatic.Toggled += async (_, _) => await GuardAsync(async () =>
            await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { MaintenanceEnabled = _automatic.IsToggled } }).ConfigureAwait(true)).ConfigureAwait(true);
        var automatic = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        automatic.Add(Theme.Label(T("automatic"))); automatic.Add(_automatic, 1);
        var content = Theme.Stack(Theme.Label(T("help"), 13, Theme.Muted), automatic, _query,
            Theme.ActionButton(T("analyze"), "refresh_cw", () => GuardAsync(AnalyzeAsync), primary: true), _summary, _findings);
        content.Padding = new Thickness(Theme.PageInset);
        Content = Theme.Workspace(T("title"), ui.Get("ui.brain.title"), new ScrollView { Content = content }, () => NavigationMotion.PopAsync(Navigation));
    }
    private string T(string key) => _ui.Get("ui.maintenance." + key);
    /// <inheritdoc />
    protected override void OnAppearing()
    { base.OnAppearing(); _visible = true; _viewLifetime = new(); _maintenance.Changed += OnChanged; Render(); }
    /// <inheritdoc />
    protected override void OnDisappearing()
    { Dispose(); base.OnDisappearing(); }
    private void OnChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() => { if (_visible) { Render(); } });
    private async Task AnalyzeAsync()
    { await _maintenance.RunBatchAsync(16, _viewLifetime?.Token ?? CancellationToken.None).ConfigureAwait(true); if (_visible) { Render(); } }
    private void Render()
    {
        BrainMaintenanceReport report = _maintenance.Report;
        _summary.Text = report.Timestamp == DateTimeOffset.MinValue ? T("notAnalyzed") : string.Format(_ui.Culture, T("summary"), report.Processed, report.Remaining, report.Total, report.TotalMilliseconds.ToString("F0", _ui.Culture))
            + "\n" + T("lastChecked") + " " + report.Timestamp.ToLocalTime().ToString("g", _ui.Culture)
            + (report.SemanticAvailable ? "" : "\n" + T("lexicalOnly")) + (report.InventoryTruncated || report.FindingsTruncated || report.OmittedFindings > 0 || report.Findings.Count > 40 ? "\n" + T("bounded") : "");
        _findings.Clear(); string query = _query.Text?.Trim() ?? "";
        foreach (BrainMaintenanceFinding finding in report.Findings.Where(finding => query.Length == 0 || T(finding.Code).Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || finding.Sources.Any(source => source.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))).Take(40))
        {
            var card = Theme.Stack(Theme.Label(T(finding.Code), 15));
            if (finding.Sources.Count == 0) { card.Add(Theme.Label(finding.Evidence, 12, Theme.Muted)); }
            foreach (BrainMaintenanceReference source in finding.Sources)
            {
                BrainNote? note = _brain.Snapshot.Notes.FirstOrDefault(note => note.Id == source.Id && note.Hash == source.Hash && note.Path == source.Path);
                card.Add(Theme.ActionButton(note?.Title ?? source.Path, "file_text", async () => { await NavigationMotion.PopAsync(Navigation).ConfigureAwait(true); await _open(source.Path).ConfigureAwait(true); }));
                card.Add(Theme.Label(source.Path, 11, Theme.Muted));
            }
            if (finding.Code == "duplicate_relation" && finding.Sources.Count == 1)
            {
                Button clean = Theme.ActionButton(T("cleanRelations"), "link", () => GuardAsync(async () =>
                {
                    string vault = _brain.Location; CancellationToken token = _viewLifetime?.Token ?? throw new InvalidOperationException("Maintenance page is closed.");
                    if (!await Laura.App.Platforms.Windows.WindowsDialogs.ConfirmAsync(T("cleanRelations"), T("cleanHelp"), T("apply"), _ui.Get("ui.maui.cancel"), token).ConfigureAwait(true)) { return; }
                    await _maintenance.DeduplicateRelationsAsync(finding.Sources[0], vault, token).ConfigureAwait(true); await AnalyzeAsync().ConfigureAwait(true);
                }));
                clean.AutomationId = "brain-maintenance-clean-" + finding.Id; card.Add(clean);
            }
            if (finding.Code == "duplicate_content" && finding.Sources.Count == 2 && finding.Sources.All(source => _brain.Snapshot.Notes.Any(note => note.Id == source.Id && note.Hash == source.Hash && note.IsKnowledge)))
            { Button review = Theme.ActionButton(T("reviewDuplicate"), "archive", () => GuardAsync(() => ArchiveAsync(finding))); review.AutomationId = "brain-maintenance-duplicate-" + finding.Id; card.Add(review); }
            _findings.Add(Theme.Card(card, 16));
        }
        if (_findings.Count == 0 && report.Timestamp != DateTimeOffset.MinValue) { _findings.Add(Theme.Label(T("empty"), 13, Theme.Muted)); }
        _findings.Add(Theme.Label(T("reviewHelp"), 12, Theme.Muted));
    }
    private async Task ArchiveAsync(BrainMaintenanceFinding finding)
    {
        string vault = _brain.Location;
        CancellationToken token = _viewLifetime?.Token ?? throw new InvalidOperationException("Maintenance page is closed.");
        string[] choices = finding.Sources.Select(source => _brain.Snapshot.Notes.First(note => note.Id == source.Id && note.Path == source.Path).Title + " · " + source.Path).ToArray();
        string? keep = await Laura.App.Platforms.Windows.WindowsDialogs.ChooseAsync(T("keep"), _ui.Get("ui.maui.cancel"), null, token, choices).ConfigureAwait(true);
        int index = Array.IndexOf(choices, keep); if (index < 0) { return; }
        if (!await Laura.App.Platforms.Windows.WindowsDialogs.ConfirmAsync(T("reviewDuplicate"), T("archiveHelp"), T("archive"), _ui.Get("ui.maui.cancel"), token).ConfigureAwait(true)) { return; }
        await _maintenance.ArchiveDuplicateAsync(finding.Sources[1 - index], finding.Sources[index], vault, token).ConfigureAwait(true); await AnalyzeAsync().ConfigureAwait(true);
    }
    private async Task GuardAsync(Func<Task> action)
    {
        if (_busy) { return; } _busy = true;
        try { await action().ConfigureAwait(true); }
        catch (OperationCanceledException) { if (_visible) { _summary.Text = T("cancelled"); } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException or TimeoutException) { _summary.Text = T("changed"); }
        finally { _busy = false; }
    }
    /// <summary>Runs the actual local analysis action only in isolated UI/Brain diagnostics.</summary>
    public Task AnalyzeForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Any(argument => argument is "--ui-smoke" or "--brain-smoke")) { throw new InvalidOperationException("Requires isolated diagnostics."); }
        return AnalyzeAsync();
    }
    /// <inheritdoc />
    public void Dispose()
    { _visible = false; _maintenance.Changed -= OnChanged; _viewLifetime?.Cancel(); _viewLifetime?.Dispose(); _viewLifetime = null; GC.SuppressFinalize(this); }
}

using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Brain;

namespace Laura.App.Views;

/// <summary>Progressive local inspector for evidence, exact procedural readiness, native outcomes and preserved revisions. It never activates or executes anything.</summary>
public sealed class BrainEvidenceView : ContentView
{
    private readonly BrainNote _note;
    private readonly BrainService _brain;
    private readonly BrainSkills _skills;
    private readonly BrainSkillUsage _usage;
    private readonly IUiLocalizer _ui;
    private readonly Func<string, Task> _open;
    private readonly string _vault;
    private int _refreshVersion;
    private readonly Label _state = Theme.Label("", 13), _outcomes = Theme.Label("", 12, Theme.Muted);
    private readonly VerticalStackLayout _requirements = new() { Spacing = 8 }, _sources = new() { Spacing = 8 }, _history = new() { Spacing = 8 };
    /// <summary>Builds small expandable native sections from actual source metadata, without loading all skill instructions.</summary>
    public BrainEvidenceView(BrainNote note, BrainService brain, BrainSkills skills, BrainSkillUsage usage, IUiLocalizer ui, Func<string, Task> open)
    {
        _note = note; _brain = brain; _skills = skills; _usage = usage; _ui = ui; _open = open;
        VerticalOptions = LayoutOptions.Start; Margin = new Thickness(8, 0);
        _vault = brain.Location;
        _state.AutomationId = "brain-evidence-state"; _outcomes.AutomationId = "brain-evidence-outcomes";
        string evidence = note.Source == "native_process_creation" ? T("observedLaunch") : note.Verification switch { "user_reported" => T("userReported"), "unverified" => T("unverified"), "observed_execution" => T("observedExecution"), "observed_process_creation" => T("observedLaunch"), "verified" => T("authoredVerified"), _ => T("unspecified") };
        var root = Theme.Stack(Theme.Caption(T("evidence")), Theme.Label(evidence, 12, Theme.Muted));
        if (note.Verification is "observed_execution" or "observed_process_creation") { root.Add(Theme.Label(T("goalUnverified"), 11, Theme.Muted)); }
        if (note.Type == "skill")
        {
            root.Add(_state); root.Add(Theme.ActionButton(T("refresh"), "refresh_cw", RefreshAsync));
            root.Add(Expandable("requirements", _requirements));
            if (note.Procedure is not null) { root.Add(Expandable("sources", _sources)); }
            root.Add(Expandable("outcomes", Theme.Stack(_outcomes, Theme.Label(T("outcomesHelp"), 11, Theme.Muted))));
        }
        root.Add(Expandable("history", _history)); Content = root; Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }
    private string T(string key) => _ui.Get("ui.evidence." + key);
    private View Expandable(string key, View content)
    {
        content.IsVisible = false; Button action = Theme.ActionButton(T(key), "chevron_down", () => { content.IsVisible = !content.IsVisible; return Task.CompletedTask; });
        action.AutomationId = "brain-evidence-" + key; return Theme.Stack(action, content);
    }
    /// <summary>Rechecks real local availability and asynchronously reads retained observations. Stale selected versions are reported instead of silently trusted.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            int version = ++_refreshVersion; if (_brain.Location != _vault) { _state.Text = T("unavailable"); _outcomes.Text = T("unavailable"); return; }
            _requirements.Clear(); _sources.Clear(); _history.Clear();
            if (_note.Type == "skill")
            {
                BrainSkillAvailability available = _skills.Inspect(_note);
                _state.Text = T(available.State) + "\n" + T(available.Approved ? "approvedVersion" : "notApprovedVersion");
                if (available.Missing.Count == 0) { _requirements.Add(Theme.Label(T("declaredReady"), 12, Theme.Muted)); }
                foreach (string missing in available.Missing.Take(12)) { _requirements.Add(Theme.Label(Reason(missing), 12, Theme.Muted)); }
                foreach (string tool in (_note.Procedure?.Tools ?? []).Take(8)) { _requirements.Add(Theme.Label(T("tool") + " " + Shorten(tool, 140), 11, Theme.Muted)); }
                foreach (BrainSkillReference reference in (_note.Procedure?.Sources ?? []).Take(8)) { AddSource(reference, false); }
                foreach (BrainSkillReference reference in (_note.Procedure?.Components ?? []).Take(4)) { AddSource(reference, true); }
                IReadOnlyList<BrainSkillOutcome> rows = await Task.Run(() => _usage.Read(_note.Id)).ConfigureAwait(true);
                if (version != _refreshVersion || _brain.Location != _vault) { return; }
                BrainSkillOutcome[] current = rows.Where(row => row.SkillHash == _note.Hash).ToArray();
                _outcomes.Text = current.Length == 0 ? T("noOutcomes") : string.Format(_ui.Culture, T("outcomeSummary"), current.Length, current.Count(row => row.ExitCode == 0), current.Count(row => row.ExitCode != 0));
                foreach (BrainSkillOutcome row in current.Take(3)) { _outcomes.Text += "\n" + row.Timestamp.ToLocalTime().ToString("g", _ui.Culture) + " · " + T("exitCode") + " " + row.ExitCode.ToString(_ui.Culture); }
                if (rows.Any(row => row.SkillHash != _note.Hash)) { _outcomes.Text += "\n" + T("olderOutcomes"); }
            }
            BrainNote[] history = _brain.Snapshot.Notes.Where(note => note.Path.StartsWith("Archive/Revisions/", StringComparison.OrdinalIgnoreCase) && note.Relations.Any(relation => relation.Target == _note.Id && relation.Type == "derived_from"))
                .OrderByDescending(note => note.UpdatedAt).Take(8).ToArray();
            if (history.Length == 0) { _history.Add(Theme.Label(T("noHistory"), 12, Theme.Muted)); }
            foreach (BrainNote revision in history)
            {
                Button action = Theme.ActionButton(revision.UpdatedAt.ToLocalTime().ToString("g", _ui.Culture), "clock", () => OpenSourceAsync(revision.Path)); action.VerticalOptions = LayoutOptions.Start;
                action.AutomationId = "brain-evidence-revision-" + revision.Id;
                Theme.DescribeAction(action, T("openRevision") + " · " + revision.Path); _history.Add(action);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { _state.Text = T("unavailable"); _outcomes.Text = T("unavailable"); }
    }
    private string Reason(string reason)
    {
        int colon = reason.IndexOf(':'); string code = colon >= 0 ? reason[..colon] : reason; string value = colon >= 0 ? reason[(colon + 1)..] : "";
        string key = code switch { "unverified_dependency" => "dependencyMissing", "unverified_prerequisite" => "prerequisiteMissing", "unavailable_tool" => "toolMissing", "source_version" => "sourceChanged", "component_review" => "componentReview", "composition_cycle_or_depth" => "compositionInvalid", "current_skill_version" => "versionChanged", "invalid_procedure_metadata" => "invalidMetadata", "external_runtime_not_verified" => "runtimeMissing", _ => "unavailable" };
        BrainNote? referenced = _brain.Snapshot.Notes.FirstOrDefault(note => note.Id == value);
        return T(key) + (value.Length > 0 ? " · " + Shorten(referenced?.Title ?? value, 140) : "");
    }
    private void AddSource(BrainSkillReference reference, bool component)
    {
        BrainNote[] matches = _brain.Snapshot.Notes.Where(note => note.Id == reference.Id).ToArray(); BrainNote? source = matches.Length == 1 ? matches[0] : null;
        _sources.Add(Theme.Label(T(component ? "component" : "source") + " · " + (source?.Hash == reference.Hash ? T("currentSource") : T("sourceChanged")), 12, Theme.Muted));
        if (source is not null) { Button open = Theme.ActionButton(source.Title, "file_text", () => OpenSourceAsync(source.Path)); open.VerticalOptions = LayoutOptions.Start; _sources.Add(open); }
        else { _sources.Add(Theme.Label(T("unavailable"), 12, Theme.Muted)); }
        if (reference.Evidence.Length > 0) { _sources.Add(Theme.Label(T("quotedEvidence") + "\n“" + Shorten(reference.Evidence, 350) + "”", 11, Theme.Muted)); }
        _sources.Add(Theme.Label(T("version") + " " + Shorten(reference.Hash, 12), 10, Theme.Muted));
    }
    private Task OpenSourceAsync(string path) { if (_brain.Location == _vault) { return _open(path); } _state.Text = T("unavailable"); return Task.CompletedTask; }
    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}

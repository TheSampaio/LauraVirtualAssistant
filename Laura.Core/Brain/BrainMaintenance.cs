using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>An exact source identity for a maintenance suggestion; it never grants permission or asserts that semantic neighbors are equivalent.</summary>
public sealed record BrainMaintenanceReference(string Id, string Hash, string Path);
/// <summary>A bounded review finding with code-defined evidence and current source versions, never an automatic knowledge edit.</summary>
public sealed record BrainMaintenanceFinding(string Id, string Code, IReadOnlyList<BrainMaintenanceReference> Sources, string Evidence);
/// <summary>A bounded incremental analysis report. Remaining work and omitted findings are explicit.</summary>
public sealed record BrainMaintenanceReport(int Processed, int Remaining, int Total, bool InventoryTruncated, bool SemanticAvailable,
    IReadOnlyList<BrainMaintenanceFinding> Findings, int OmittedFindings, double Milliseconds, DateTimeOffset Timestamp)
{
    /// <summary>Gets whether the internal finding bound was reached; omitted counts then represent a lower bound.</summary>
    public bool FindingsTruncated { get; init; }
    /// <summary>Gets total elapsed time including queueing, the existing vault/index refresh and analysis.</summary>
    public double TotalMilliseconds { get; init; }
}

/// <summary>Incremental, non-destructive quality analysis using existing snapshots, image references and retrieval. Human-reviewed exact relation cleanup uses ordinary authored revisions.</summary>
public sealed class BrainMaintenance(BrainService brain, BrainSkills skills, ISettingsService settings, ILogger<BrainMaintenance> logger, TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _startGate = new();
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrainMaintenanceFinding> _findings = new(StringComparer.Ordinal);
    private string _vault = "";
    private Task? _worker;
    private int _indexRecoveries;
    private bool _saturated;
    private int _stopping;
    private sealed record Cached(BrainMaintenanceReference Reference, string Type, string ContentKey, string ProcedureKey, string ContextKey, DateTime Day, bool Images);
    private BrainMaintenanceReport _report = new(0, 0, 0, false, false, [], 0, 0, DateTimeOffset.MinValue);
    /// <summary>Gets the last bounded derived report. Apply operations always refresh/revalidate exact source versions.</summary>
    public BrainMaintenanceReport Report
    {
        get
        {
            BrainMaintenanceReport report = Volatile.Read(ref _report); if (brain.Location != _vault) { return report with { Findings = [], Processed = 0, Remaining = 0, Total = 0, Timestamp = DateTimeOffset.MinValue }; }
            var current = brain.Snapshot.Notes.ToDictionary(note => note.Path, StringComparer.Ordinal);
            return report with { Findings = report.Findings.Where(finding => finding.Sources.All(reference => current.TryGetValue(reference.Path, out BrainNote? note) && note.Hash == reference.Hash && note.Id == reference.Id)).ToArray() };
        }
    }
    /// <summary>Publishes completed analysis without carrying note bodies in diagnostics.</summary>
    public event EventHandler? Changed;
    /// <summary>Produces bounded review metadata only, without loading note bodies or granting model tools access to consolidation edits.</summary>
    public string Describe(int budget = 5000)
    {
        budget = Math.Clamp(budget, 500, 16000); BrainMaintenanceReport report = Report; var entries = new List<object>();
        foreach (BrainMaintenanceFinding finding in report.Findings.Take(20))
        {
            entries.Add(new { finding.Id, finding.Code, sources = finding.Sources.Select(source => new { source.Id, source.Hash, path = Clip(source.Path, 320) }), evidence = Clip(finding.Evidence, 300) });
            if (Serialize().Length > budget) { entries.RemoveAt(entries.Count - 1); }
        }
        return Serialize();
        string Serialize() => JsonSerializer.Serialize(new { report.Processed, report.Remaining, report.Total, report.Timestamp, report.SemanticAvailable, report.InventoryTruncated, report.FindingsTruncated,
            findings = entries, omitted = report.OmittedFindings + report.Findings.Count - entries.Count, edits = "human_review_only; semantic similarity and age never establish facts or permissions" });
        static string Clip(string value, int count) => value.Length <= count ? value : value[..count];
    }
    /// <summary>Starts optional analysis every ten minutes. Disabled by default and independent of conversation/model calls; findings are never applied automatically.</summary>
    public void Start()
    {
        lock (_startGate) { ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this); _worker ??= Task.Run(WorkAsync); }
    }
    private async Task WorkAsync()
    {
        using var timer = new PeriodicTimer(interval ?? TimeSpan.FromMinutes(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!settings.Current.Brain.MaintenanceEnabled) { continue; }
                try { await RunBatchAsync(8, _lifetime.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or JsonException or TimeoutException or Microsoft.Data.Sqlite.SqliteException)
                { logger.LogWarning("Brain maintenance analysis unavailable ({ErrorType}); authored knowledge was not changed.", error.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    /// <summary>Analyzes at most 64 changed notes per request with a cooperative 750 ms analysis budget. The existing vault scan/index reconciliation remains separate.</summary>
    public async Task<BrainMaintenanceReport> RunBatchAsync(int limit = 16, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        var totalWatch = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); cancellationToken = linked.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
            await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false); string vault = brain.Location; BrainSnapshot snapshot = brain.Snapshot;
            if (_vault != vault) { _cache.Clear(); _findings.Clear(); _vault = vault; _saturated = false; }
            BrainNote[] all = snapshot.Notes.Where(note => note.Status != "archived" && !note.Path.StartsWith("Archive/", StringComparison.OrdinalIgnoreCase) && note.Path != "README.md").OrderBy(note => note.Path, StringComparer.Ordinal).ToArray();
            BrainNote[] notes = all.Take(10000).ToArray(); var current = notes.ToDictionary(note => note.Path, StringComparer.Ordinal);
            var identities = snapshot.Notes.GroupBy(note => note.Id, StringComparer.Ordinal).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            foreach (string removed in _cache.Keys.Where(path => !current.ContainsKey(path)).ToArray()) { _cache.Remove(removed); }
            foreach (string stale in _findings.Where(pair => pair.Value.Sources.Any(reference => !current.TryGetValue(reference.Path, out BrainNote? note) || note.Id != reference.Id || note.Hash != reference.Hash)).Select(pair => pair.Key).ToArray()) { _findings.Remove(stale); }
            foreach (string global in _findings.Where(pair => pair.Value.Sources.Count == 0 && pair.Value.Code != "derived_index_rebuilt").Select(pair => pair.Key).ToArray()) { _findings.Remove(global); }
            var files = (await brain.FilesAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string inventory = FileBrainStorage.Hash(string.Join('\n', files.Order(StringComparer.Ordinal)));
            string Context(BrainNote note, bool images) => (images ? inventory : "") + string.Join('|', (note.Procedure?.Sources ?? []).Select(source => identities.GetValueOrDefault(source.Id)?.Hash ?? "missing_or_conflicting"));
            bool Pending(BrainNote note) => !_cache.TryGetValue(note.Path, out Cached? cached) || cached.Reference.Hash != note.Hash || cached.Day != DateTime.UtcNow.Date || cached.ContextKey != Context(note, cached.Images);
            var watch = Stopwatch.StartNew(); int processed = 0;
            foreach (BrainNote note in notes.Where(Pending).Take(Math.Clamp(limit, 1, 64)))
            {
                cancellationToken.ThrowIfCancellationRequested(); if (processed > 0 && watch.ElapsedMilliseconds >= 750) { break; }
                foreach (string prior in _findings.Where(pair => pair.Value.Sources.Any(reference => reference.Path == note.Path)).Select(pair => pair.Key).ToArray()) { _findings.Remove(prior); }
                var reference = new BrainMaintenanceReference(note.Id, note.Hash, note.Path);
                string content = note.Body.Trim().ReplaceLineEndings("\n");
                string procedure = note.Procedure is { } definition ? JsonSerializer.Serialize(new { definition.Purpose, Capabilities = definition.Capabilities.Order(StringComparer.Ordinal), Tools = definition.Tools.Order(StringComparer.Ordinal), Dependencies = definition.Dependencies.Order(StringComparer.Ordinal) }) : "";
                bool images = note.Body.Contains("![", StringComparison.Ordinal);
                var cached = new Cached(reference, note.Type, content.Length >= 24 ? FileBrainStorage.Hash(content) : "", procedure.Length > 0 ? FileBrainStorage.Hash(procedure) : "", Context(note, images), DateTime.UtcNow.Date, images);
                if (snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id")) { Add("conflicting_id", [reference], "Duplicate stable identity requires explicit conflict review."); _cache[note.Path] = cached; processed++; continue; }
                foreach (BrainIssue issue in snapshot.Issues.Where(issue => issue.Path == note.Path)) { Add(issue.Code, [reference], "Reported by the existing coherent vault integrity scan."); }
                if (note.Body.Length > 64000) { Add("oversized_document", [reference], "Body characters=" + note.Body.Length.ToString(CultureInfo.InvariantCulture) + "; review progressive sections without deleting the source."); }
                if (note.Relations.Count != note.Relations.Distinct().Count()) { Add("duplicate_relation", [reference], "Only relations equal in target, type, weight, confidence, evidence and provenance can be deduplicated safely."); }
                if (note.Relations.Any(relation => relation.Type == "contradicts")) { Add("unresolved_conflict", [reference], "An authored contradiction remains for human review; neither side was selected automatically."); }
                if (note.Type == "skill" && skills.Inspect(note).Missing.Any(reason => reason.StartsWith("source_version:", StringComparison.Ordinal)))
                { Add("stale_skill_source", [reference], "Declared procedural evidence no longer matches an actual current source version; renewed review is required."); }
                if (note.IsKnowledge && note.Type is "project" or "goal" or "application" && DateTimeOffset.UtcNow - note.UpdatedAt > TimeSpan.FromDays(365))
                { Add("review_age", [reference], "Older than 365 days; age alone does not establish obsolescence."); }
                foreach (string image in BrainService.ImageReferences(note.Body, note.Path).Where(image => !files.Contains(image)).Take(8)) { Add("missing_image", [reference], "Missing vault-local image: " + image); }
                foreach (Cached candidate in _cache.Values.Where(candidate => candidate.Reference.Id != note.Id && candidate.Type == note.Type && current.TryGetValue(candidate.Reference.Path, out BrainNote? actual) && actual.Hash == candidate.Reference.Hash).Take(10000))
                {
                    if (cached.ContentKey.Length > 0 && candidate.ContentKey == cached.ContentKey) { Add("duplicate_content", [reference, candidate.Reference], "Same trimmed text and line endings, same category; metadata and context still require review."); }
                    else if (cached.ProcedureKey.Length > 0 && candidate.ProcedureKey == cached.ProcedureKey) { Add("similar_procedure", [reference, candidate.Reference], "Same declared purpose, capabilities, tools and dependencies; procedure steps/sources may differ and were not merged."); }
                }
                if (note.IsKnowledge && brain.SemanticAvailable)
                {
                    string query = note.Title + "\n" + note.Body[..Math.Min(900, note.Body.Length)];
                    foreach (BrainHit hit in (await brain.SearchAsync(query, 8, cancellationToken: cancellationToken).ConfigureAwait(false)).Where(hit => hit.Note.Id != note.Id && hit.Note.Type == note.Type && hit.Semantic >= .9).Take(3))
                    { Add("semantic_neighbor", [reference, new(hit.Note.Id, hit.Note.Hash, hit.Note.Path)], "Local cosine=" + hit.Semantic.ToString("F3", CultureInfo.InvariantCulture) + "; candidate for review, not factual equivalence or a new relation."); }
                }
                else if (note.Type == "skill" && brain.SemanticAvailable && note.Description.Length > 0)
                {
                    string metadata = await skills.DiscoverAsync(note.Description, 6000, cancellationToken).ConfigureAwait(false);
                    if (metadata.Length > 0)
                    {
                        using var result = JsonDocument.Parse(metadata);
                        foreach (JsonElement hit in result.RootElement.GetProperty("skills").EnumerateArray().Where(hit => hit.GetProperty("semantic").GetDouble() >= .9).Take(4))
                        {
                            string id = hit.GetProperty("id").GetString()!;
                            if (id != note.Id && identities.TryGetValue(id, out BrainNote? other) && other.Hash == hit.GetProperty("hash").GetString())
                            { Add("similar_skill_metadata", [reference, new(other.Id, other.Hash, other.Path)], "Local semantic metadata neighbor; full steps were not loaded and procedural equivalence remains unverified."); }
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested(); _cache[note.Path] = cached; processed++;
            }
            foreach (BrainIssue issue in snapshot.Issues.Where(issue => !current.ContainsKey(issue.Path)).Take(32)) { Add(issue.Code, [], "Existing vault scan: " + issue.Path + "; unreadable/unresolved file preserved without automatic rewrite."); }
            if (brain.IndexRecoveries > _indexRecoveries && brain.LastIndexRecoveryVaultIdentity == FileBrainStorage.Hash(vault)) { Add("derived_index_rebuilt", [], "Existing index recovered a corrupt derived cache; Markdown remains the source of truth."); }
            _indexRecoveries = brain.IndexRecoveries;
            if (brain.Location != vault) { _cache.Clear(); _findings.Clear(); throw new IOException("Vault changed during maintenance analysis."); }
            int remaining = notes.Count(Pending);
            BrainMaintenanceFinding[] findings = _findings.Values.OrderBy(finding => Rank(finding.Code)).ThenBy(finding => finding.Id, StringComparer.Ordinal).ToArray();
            var report = new BrainMaintenanceReport(processed, remaining, all.Length, all.Length > notes.Length, brain.SemanticAvailable, findings.Take(200).ToArray(), Math.Max(0, findings.Length - 200), watch.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow) { FindingsTruncated = _saturated, TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds };
            Volatile.Write(ref _report, report); return report;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private void Add(string code, IReadOnlyList<BrainMaintenanceReference> sources, string evidence)
    {
        string id = FileBrainStorage.Hash(code + "\n" + string.Join('\n', sources.OrderBy(source => source.Path, StringComparer.Ordinal).Select(source => source.Path + ":" + source.Hash)) + "\n" + evidence);
        if (_findings.Count < 2000 || _findings.ContainsKey(id)) { _findings[id] = new(id, code, sources.ToArray(), evidence); }
        else
        {
            _saturated = true; BrainMaintenanceFinding worst = _findings.Values.MaxBy(finding => Rank(finding.Code))!;
            if (Rank(code) < Rank(worst.Code)) { _findings.Remove(worst.Id); _findings[id] = new(id, code, sources.ToArray(), evidence); }
        }
    }
    private static int Rank(string code) => code switch
    { "conflicting_id" or "invalid_or_unreadable" => 0, "unresolved_conflict" or "stale_skill_source" => 1,
        "unresolved_relation" or "ambiguous_or_unresolved_link" or "missing_image" => 2, "duplicate_relation" => 3, "duplicate_content" => 4, "derived_index_rebuilt" => 5, "oversized_document" => 6, "review_age" => 7, _ => 8 };
    /// <summary>Removes only exactly equal duplicate relations after native human confirmation. Exact hash/vault revalidation and existing revision storage protect concurrent edits.</summary>
    public async Task<BrainNote> DeduplicateRelationsAsync(BrainMaintenanceReference reference, string expectedVault, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (brain.Location != expectedVault || expectedVault != _vault) { throw new IOException("Review maintenance in the current vault."); }
            await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            BrainNote note = brain.Snapshot.Notes.SingleOrDefault(note => note.Id == reference.Id && note.Hash == reference.Hash && note.Path == reference.Path)
                ?? throw new IOException("Maintenance source changed; review its current version.");
            if (brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id")) { throw new IOException("Resolve conflicting identity before editing relations."); }
            BrainRelation[] unique = note.Relations.Distinct().ToArray(); if (unique.Length == note.Relations.Count) { return note; }
            return await brain.SaveReviewedAsync(note with { Relations = unique }, expectedVault, [(note.Path, note.Hash)], cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>Reversibly archives one human-selected, exactly text-identical knowledge duplicate, retaining its original ID/path, body, relations and revision. Similarity never qualifies.</summary>
    public async Task<BrainNote> ArchiveDuplicateAsync(BrainMaintenanceReference duplicate, BrainMaintenanceReference canonical, string expectedVault, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (brain.Location != expectedVault || expectedVault != _vault) { throw new IOException("Review duplicates in the current vault."); }
            await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            BrainNote Match(BrainMaintenanceReference reference) => brain.Snapshot.Notes.SingleOrDefault(note => note.Id == reference.Id && note.Hash == reference.Hash && note.Path == reference.Path && note.IsKnowledge
                && !brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id")) ?? throw new IOException("Reviewed duplicate version changed or is unavailable.");
            BrainNote note = Match(duplicate), target = Match(canonical);
            string content = note.Body.Trim().ReplaceLineEndings("\n");
            if (note.Id == target.Id || note.Type != target.Type || note.MemoryType != target.MemoryType || note.Key != target.Key || Path.GetDirectoryName(note.Path) != Path.GetDirectoryName(target.Path)
                || content.Length < 24 || content != target.Body.Trim().ReplaceLineEndings("\n"))
            { throw new IOException("Only compatible exact knowledge duplicates can be archived by this action; semantic similarity is insufficient."); }
            var relation = new BrainRelation(target.Id, "related_to", 1, 1, "Human-reviewed identical body SHA256=" + FileBrainStorage.Hash(content), "manual_duplicate_review");
            return await brain.SaveReviewedAsync(note with { Status = "archived", Relations = note.Relations.Append(relation).Distinct().ToArray() }, expectedVault,
                [(note.Path, note.Hash), (target.Path, target.Hash)], cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) { return; }
        await _lifetime.CancelAsync().ConfigureAwait(false); if (_worker is not null) { await _worker.ConfigureAwait(false); }
        await _gate.WaitAsync().ConfigureAwait(false); _gate.Release(); _gate.Dispose(); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}

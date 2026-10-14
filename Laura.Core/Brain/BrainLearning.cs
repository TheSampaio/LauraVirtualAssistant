using System.Text.RegularExpressions;
using System.Threading.Channels;
using Laura.Core.Abstractions;
using Laura.Core.Text;
using Laura.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>Conservatively consolidates durable user declarations; never learns assertions from generated assistant text.</summary>
public sealed partial class BrainLearning(BrainService brain, ISettingsService settings, ILocalizer localizer, ILogger<BrainLearning> logger) : IAsyncDisposable
{
    private sealed record Work(string Text, string Vault, TaskCompletionSource? Barrier = null, Laura.Core.Memory.SqliteConversationMemory? Journal = null, long Receipt = 0);
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _workerGate = new();
    private readonly SemaphoreSlim _consolidation = new(1, 1);
    private Task? _worker;
    private bool _stopping;
    private long _accepted, _completed, _failed, _excluded, _saturated;
    /// <summary>Gets non-sensitive queue and processing counters for diagnostics.</summary>
    public BrainLearningStatus Status => new(Interlocked.Read(ref _accepted), Interlocked.Read(ref _completed), Interlocked.Read(ref _failed), Interlocked.Read(ref _excluded), Interlocked.Read(ref _saturated));
    /// <summary>Queues only bounded user declarations, preserving responsive speech and UI paths.</summary>
    public BrainObservationResult Observe(string text)
    {
        if (!Eligible(text)) { Interlocked.Increment(ref _excluded); return BrainObservationResult.Excluded; }
        lock (_workerGate)
        {
            if (_stopping) { return BrainObservationResult.Stopped; }
            StartWorker();
            if (_queue.Writer.TryWrite(new(text, brain.Location))) { Interlocked.Increment(ref _accepted); return BrainObservationResult.Accepted; }
            Interlocked.Increment(ref _saturated);
            logger.LogWarning("Brain learning queue saturated; event was not accepted. Retained transcript remains available.");
            return BrainObservationResult.Saturated;
        }
    }
    /// <summary>Waits for capacity instead of silently dropping relevant work; cancellation means it was not accepted.</summary>
    public async Task<BrainObservationResult> ObserveAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!Eligible(text)) { Interlocked.Increment(ref _excluded); return BrainObservationResult.Excluded; }
        lock (_workerGate) { if (_stopping) { return BrainObservationResult.Stopped; } StartWorker(); }
        try { await _queue.Writer.WriteAsync(new(text, brain.Location), cancellationToken).ConfigureAwait(false); Interlocked.Increment(ref _accepted); return BrainObservationResult.Accepted; }
        catch (ChannelClosedException) { return BrainObservationResult.Stopped; }
    }
    /// <summary>Waits until previously accepted work has settled. Failed work remains visible in <see cref="Status"/>.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? recovery;
        lock (_workerGate) { recovery = _recovery; }
        if (recovery is not null) { await recovery.WaitAsync(cancellationToken).ConfigureAwait(false); }
        lock (_workerGate) { ObjectDisposedException.ThrowIf(_stopping, this); StartWorker(); }
        await _queue.Writer.WriteAsync(new("", "", barrier), cancellationToken).ConfigureAwait(false);
        await barrier.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private bool Eligible(string text) => settings.Current.RememberConversations && settings.Current.Brain.AutoLearn && text.Length is >= 8 and <= 4000 && !Sensitive(text);
    /// <summary>Checks whether user settings and privacy allow an automatic-learning receipt.</summary>
    public bool CanObserve(string text) => Eligible(text);
    private void StartWorker() => _worker ??= Task.Run(RunAsync);
    /// <summary>Gets whether a declaration contains credential, financial, health or private-contact markers requiring deliberate manual storage.</summary>
    public static bool Sensitive(string text) => SensitivePattern().IsMatch(text);
    /// <summary>Processes grounded durable statements rather than producing one note for every chat message.</summary>
    public Task LearnAsync(string text, CancellationToken cancellationToken = default) => LearnAtLocationAsync(text, brain.Location, cancellationToken);
    private async Task LearnAtLocationAsync(string text, string vault, CancellationToken cancellationToken)
    {
        if (!Eligible(text)) { return; }
        await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
        foreach ((string key, string type, string folder) in new[] { ("editor", "preference", "Owner/Preferences"), ("language", "preference", "Owner/Preferences"),
            ("style", "preference", "Owner/Preferences"), ("goal", "goal", "Owner/Goals"), ("project", "project", "Projects"), ("decision", "decision", "Memories/Decisions"), ("routine", "procedure", "Procedures") })
        {
            string pattern = localizer.Get(LocalizationKeys.Brain.LearningPrefix + key);
            Match match = Regex.Match(text.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) { continue; }
            string value = match.Groups["value"].Value.Trim().TrimEnd('.', '!');
            if (value.Length is < 2 or > 500) { continue; }
            string stableKey = "owner." + key + (key is "goal" or "project" or "decision" or "routine" ? "." + FileBrainStorage.Hash(TextNormalizer.Normalize(value))[..12] : "");
            var note = new BrainNote { Key = stableKey, Title = value.Length > 90 ? value[..90] : value, Type = type,
                MemoryType = key is "decision" ? "episodic" : key is "routine" ? "procedural" : "semantic",
                Path = folder + "/" + BrainNaming.FileName(key is "editor" or "language" or "style" ? key : value, stableKey), Body = text.Trim(), Source = "user_declaration", Verification = "user_reported", Tags = [key], Confidence = .8, Importance = .7 };
            var relations = new List<BrainRelation>();
            if (key is "editor" or "language")
            {
                BrainNote entity = await EntityAsync(value, vault, cancellationToken).ConfigureAwait(false);
                relations.Add(new(entity.Id, "prefers", .9, .8, text.Trim(), "user_declaration"));
            }
            if (key == "decision")
            {
                Match association = Regex.Match(value, localizer.Get(LocalizationKeys.Brain.ProjectUse), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (association.Success)
                {
                    string name = association.Groups["project"].Value.Trim().TrimEnd('.');
                    BrainNote? project = brain.Snapshot.Notes.FirstOrDefault(candidate => candidate.IsKnowledge && candidate.Type == "project" && candidate.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (project is not null)
                    {
                        BrainNote entity = await EntityAsync(association.Groups["entity"].Value.Trim(), vault, cancellationToken).ConfigureAwait(false);
                        relations.Add(new(project.Id, "belongs_to", .9, .8, text.Trim(), "user_declaration"));
                        if (!project.Relations.Any(relation => relation.Target == entity.Id && relation.Type == "uses"))
                        { await brain.SaveAtLocationAsync(project with { Relations = project.Relations.Concat([new BrainRelation(entity.Id, "uses", .9, .8, text.Trim(), "user_declaration")]).ToArray() }, vault, cancellationToken).ConfigureAwait(false); }
                        relations.Add(new(entity.Id, "uses", .9, .8, text.Trim(), "user_declaration"));
                    }
                }
            }
            await _consolidation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await ConsolidateCoreAsync(note with { Relations = relations }, false, vault, cancellationToken).ConfigureAwait(false); }
            finally { _consolidation.Release(); }
        }
    }
    /// <summary>Creates a named entity only after an explicit user statement, without asserting an invented factual definition.</summary>
    private async Task<BrainNote> EntityAsync(string name, string vault, CancellationToken cancellationToken)
    {
        BrainNote? existing = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && note.Type == "entity" && note.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
        return existing ?? await brain.SaveAtLocationAsync(new BrainNote { Path = "Knowledge/Entities/" + BrainNaming.FileName(name, TextNormalizer.Normalize(name)), Title = name, Type = "entity",
            Body = "# " + name + "\n\n" + localizer.Get(LocalizationKeys.Brain.EntityContext), Source = "user_declaration", Confidence = .8 }, vault, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Updates explicit corrections with history; ambiguous automatic contradictions preserve both versions for review.</summary>
    public async Task<BrainNote> ConsolidateAsync(BrainNote incoming, bool explicitCorrection, CancellationToken cancellationToken = default)
    {
        await _consolidation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ConsolidateCoreAsync(incoming, explicitCorrection, brain.Location, cancellationToken).ConfigureAwait(false); }
        finally { _consolidation.Release(); }
    }
    private async Task<BrainNote> ConsolidateCoreAsync(BrainNote incoming, bool explicitCorrection, string vault, CancellationToken cancellationToken)
    {
        await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(vault, brain.Location, StringComparison.Ordinal)) { throw new InvalidOperationException("Learning vault changed."); }
        BrainNote? existing = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && incoming.Key.Length > 0 && note.Key == incoming.Key);
        existing ??= brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && TextNormalizer.Normalize(note.Body) == TextNormalizer.Normalize(incoming.Body));
        if (existing is null) { return await brain.SaveAtLocationAsync(incoming, vault, cancellationToken).ConfigureAwait(false); }
        if (TextNormalizer.Normalize(existing.Body) == TextNormalizer.Normalize(incoming.Body)
            || (!explicitCorrection && existing.Key == incoming.Key && TextNormalizer.Normalize(existing.Title) == TextNormalizer.Normalize(incoming.Title))) { return existing; }
        if (explicitCorrection)
        {
            return await brain.SaveAtLocationAsync(existing with { Body = incoming.Body, Title = incoming.Title, UpdatedAt = DateTimeOffset.UtcNow,
                Source = incoming.Source, Verification = incoming.Verification, Confidence = incoming.Confidence, Relations = incoming.Relations.Count > 0 ? incoming.Relations : existing.Relations }, vault, cancellationToken).ConfigureAwait(false);
        }
        var conflict = incoming with { Id = Guid.NewGuid().ToString("D"), Path = "Inbox/" + Guid.NewGuid().ToString("N") + ".md", Status = "draft",
            Relations = incoming.Relations.Concat([new BrainRelation(existing.Id, "contradicts", 1, incoming.Confidence, incoming.Body, incoming.Source)]).ToArray() };
        BrainNote? already = brain.Snapshot.Notes.FirstOrDefault(note => note.Status == "draft" && note.Key == conflict.Key && TextNormalizer.Normalize(note.Body) == TextNormalizer.Normalize(conflict.Body));
        return already ?? await brain.SaveAtLocationAsync(conflict, vault, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Drains a bounded queue and isolates failures without logging text, keys or private paths.</summary>
    private async Task RunAsync()
    {
        try
        {
            await foreach (Work work in _queue.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (work.Barrier is not null) { work.Barrier.TrySetResult(); continue; }
                bool settled = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (!string.Equals(work.Vault, brain.Location, StringComparison.Ordinal)) { throw new InvalidOperationException("Learning vault changed."); }
                        string vaultIdentity = FileBrainStorage.Hash(work.Vault);
                        if (work.Journal is null || work.Journal.HasPendingLearning(work.Receipt, vaultIdentity))
                        {
                            await LearnAtLocationAsync(work.Text, work.Vault, _lifetime.Token).ConfigureAwait(false);
                            work.Journal?.CompleteLearning(work.Receipt, vaultIdentity);
                        }
                        settled = true; Interlocked.Increment(ref _completed); break;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or RegexMatchTimeoutException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
                    {
                        bool retry = attempt < 2 && error is IOException or Microsoft.Data.Sqlite.SqliteException;
                        if (!retry) { Interlocked.Increment(ref _failed); logger.LogWarning("Brain learning failed ({ErrorType}); retained transcript may be retried.", error.GetType().Name); break; }
                        await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), _lifetime.Token).ConfigureAwait(false);
                    }
                }
                if (settled && work.Journal is not null)
                {
                    lock (_workerGate) { _scheduledReceipts.Remove(FileBrainStorage.Hash(work.Vault) + ":" + work.Receipt.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                    RecoverPending(work.Journal);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { while (_queue.Reader.TryRead(out Work? work)) { work.Barrier?.TrySetCanceled(); } }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_workerGate) { if (_stopping) { return; } _stopping = true; _queue.Writer.TryComplete(); worker = _worker; }
        if (worker is not null)
        {
            try { await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
                logger.LogWarning("Brain learning shutdown timed out; {Pending} accepted events were not completed.", Status.Pending);
                await worker.ConfigureAwait(false);
            }
        }
        if (_recovery is not null) { await _recovery.ConfigureAwait(false); }
        _lifetime.Dispose(); _consolidation.Dispose(); GC.SuppressFinalize(this);
    }
    /// <summary>Excludes secrets and sensitive classes from automatic capture rather than pretending they are safe ordinary facts.</summary>
    [GeneratedRegex(@"password|senha|secret|api.?key|token|credit.?card|cart[aã]o|cpf|diagn[oó]stic|medica|sa[uú]de|endere[cç]o|address|\b\d{3}[.\-]?\d{3}[.\-]?\d{3}[\-]?\d{2}\b|[\w.+-]+@[\w.-]+\.[\w]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePattern();
}

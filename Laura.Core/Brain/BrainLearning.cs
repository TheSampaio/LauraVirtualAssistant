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
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _worker;
    private int _started;
    /// <summary>Queues only bounded user declarations, preserving responsive speech and UI paths.</summary>
    public void Observe(string text)
    {
        if (!settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn || text.Length is < 8 or > 4000 || Sensitive(text)) { return; }
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0) { _worker = Task.Run(RunAsync); } _queue.Writer.TryWrite(text);
    }
    /// <summary>Gets whether a declaration contains credential, financial, health or private-contact markers requiring deliberate manual storage.</summary>
    public static bool Sensitive(string text) => SensitivePattern().IsMatch(text);
    /// <summary>Processes grounded durable statements rather than producing one note for every chat message.</summary>
    public async Task LearnAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn || Sensitive(text)) { return; }
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
                Path = folder + "/" + BrainNaming.FileName(key is "editor" or "language" or "style" ? key : value, stableKey), Body = text.Trim(), Source = "user_declaration", Tags = [key], Confidence = .8, Importance = .7 };
            var relations = new List<BrainRelation>();
            if (key is "editor" or "language")
            {
                BrainNote entity = await EntityAsync(value, cancellationToken).ConfigureAwait(false);
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
                        BrainNote entity = await EntityAsync(association.Groups["entity"].Value.Trim(), cancellationToken).ConfigureAwait(false);
                        relations.Add(new(project.Id, "belongs_to", .9, .8, text.Trim(), "user_declaration"));
                        if (!project.Relations.Any(relation => relation.Target == entity.Id && relation.Type == "uses"))
                        { await brain.SaveAsync(project with { Relations = project.Relations.Concat([new BrainRelation(entity.Id, "uses", .9, .8, text.Trim(), "user_declaration")]).ToArray() }, cancellationToken).ConfigureAwait(false); }
                        relations.Add(new(entity.Id, "uses", .9, .8, text.Trim(), "user_declaration"));
                    }
                }
            }
            await ConsolidateAsync(note with { Relations = relations }, false, cancellationToken).ConfigureAwait(false);
        }
    }
    /// <summary>Creates a named entity only after an explicit user statement, without asserting an invented factual definition.</summary>
    private async Task<BrainNote> EntityAsync(string name, CancellationToken cancellationToken)
    {
        BrainNote? existing = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && note.Type == "entity" && note.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
        return existing ?? await brain.SaveAsync(new BrainNote { Path = "Knowledge/Entities/" + BrainNaming.FileName(name, TextNormalizer.Normalize(name)), Title = name, Type = "entity",
            Body = "# " + name + "\n\n" + localizer.Get(LocalizationKeys.Brain.EntityContext), Source = "user_declaration", Confidence = .8 }, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Updates explicit corrections with history; ambiguous automatic contradictions preserve both versions for review.</summary>
    public async Task<BrainNote> ConsolidateAsync(BrainNote incoming, bool explicitCorrection, CancellationToken cancellationToken = default)
    {
        await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BrainNote? existing = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && incoming.Key.Length > 0 && note.Key == incoming.Key);
        existing ??= brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && TextNormalizer.Normalize(note.Body) == TextNormalizer.Normalize(incoming.Body));
        if (existing is null) { return await brain.SaveAsync(incoming, cancellationToken).ConfigureAwait(false); }
        if (TextNormalizer.Normalize(existing.Body) == TextNormalizer.Normalize(incoming.Body)
            || (!explicitCorrection && existing.Key == incoming.Key && TextNormalizer.Normalize(existing.Title) == TextNormalizer.Normalize(incoming.Title))) { return existing; }
        if (explicitCorrection)
        {
            return await brain.SaveAsync(existing with { Body = incoming.Body, Title = incoming.Title, UpdatedAt = DateTimeOffset.UtcNow,
                Source = incoming.Source, Confidence = incoming.Confidence, Relations = incoming.Relations.Count > 0 ? incoming.Relations : existing.Relations }, cancellationToken).ConfigureAwait(false);
        }
        var conflict = incoming with { Id = Guid.NewGuid().ToString("D"), Path = "Inbox/" + Guid.NewGuid().ToString("N") + ".md", Status = "draft",
            Relations = incoming.Relations.Concat([new BrainRelation(existing.Id, "contradicts", 1, incoming.Confidence, incoming.Body, incoming.Source)]).ToArray() };
        BrainNote? already = brain.Snapshot.Notes.FirstOrDefault(note => note.Status == "draft" && note.Key == conflict.Key && TextNormalizer.Normalize(note.Body) == TextNormalizer.Normalize(conflict.Body));
        return already ?? await brain.SaveAsync(conflict, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Drains a bounded queue and isolates failures without logging text, keys or private paths.</summary>
    private async Task RunAsync()
    {
        try
        {
            await foreach (string text in _queue.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                try { await LearnAsync(text, _lifetime.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or RegexMatchTimeoutException)
                { logger.LogWarning("Brain learning deferred ({ErrorType}).", error.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    { _queue.Writer.TryComplete(); await _lifetime.CancelAsync().ConfigureAwait(false); if (_worker is not null) { await _worker.ConfigureAwait(false); } _lifetime.Dispose(); GC.SuppressFinalize(this); }
    /// <summary>Excludes secrets and sensitive classes from automatic capture rather than pretending they are safe ordinary facts.</summary>
    [GeneratedRegex(@"password|senha|secret|api.?key|token|credit.?card|cart[aã]o|cpf|diagn[oó]stic|medica|sa[uú]de|endere[cç]o|address|\b\d{3}[.\-]?\d{3}[.\-]?\d{3}[\-]?\d{2}\b|[\w.+-]+@[\w.-]+\.[\w]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePattern();
}

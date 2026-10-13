using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Brain;

/// <summary>Compatibility adapter: raw transcript retention stays separate; durable facts are sourced exclusively from Markdown.</summary>
public sealed class BrainConversationMemory(BrainService brain, BrainLearning learning, SqliteConversationMemory transcripts) : IConversationMemory
{
    private readonly object _gate = new();
    private bool _initialized;
    private string _lastUserText = "";
    /// <summary>Gets transient working context for grounded tool proposals; it is not an execution permission.</summary>
    public string LastUserText { get { lock (_gate) { return _lastUserText; } } }
    /// <inheritdoc />
    public string WorkingUserText => LastUserText;
    /// <inheritdoc />
    public void SetWorkingContext(string text)
    { ArgumentNullException.ThrowIfNull(text); lock (_gate) { _lastUserText = text.Length <= 4000 ? text : ""; } }
    /// <inheritdoc />
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) { return; }
            transcripts.Initialize(); brain.InitializeAsync().GetAwaiter().GetResult();
            if (!brain.Snapshot.Notes.Any(note => note.Key == "system.legacy-import"))
            {
                foreach (MemoryFact fact in transcripts.ReadAllFacts())
                {
                    if (!brain.Snapshot.Notes.Any(note => note.Key == fact.Key))
                    {
                        brain.SaveAsync(new BrainNote { Path = "Knowledge/Facts/" + BrainNaming.FileName(fact.Key, fact.Key), Key = fact.Key,
                            Title = fact.Key, Body = fact.Text, Type = "fact", Source = "legacy_import", Confidence = 1,
                            CreatedAt = fact.Updated, UpdatedAt = fact.Updated }, preserveTimestamp: true).GetAwaiter().GetResult();
                    }
                }
                brain.SaveAsync(new BrainNote { Path = "Archive/legacy-import.md", Key = "system.legacy-import", Title = "Legacy fact import receipt", Status = "archived",
                    Body = "Legacy explicit facts were copied to Markdown. The original application database was retained unchanged. Subsequent fact operations use only this vault." }).GetAwaiter().GetResult();
            }
            _initialized = true;
        }
    }
    /// <inheritdoc />
    public void Append(ConversationMessage message)
    {
        Initialize(); transcripts.Append(message);
        if (message.Source != ConversationMessageSource.Assistant)
        { SetWorkingContext(message.Text); learning.Observe(message.Text); }
    }
    /// <inheritdoc />
    public IReadOnlyList<ConversationMessage> ReadRecent(int limit) => transcripts.ReadRecent(limit);
    /// <inheritdoc />
    public IReadOnlyList<MemoryFact> Search(string query)
    {
        Initialize(); return brain.SearchAsync(query, 100).GetAwaiter().GetResult().Select(hit => new MemoryFact(hit.Note.Key.Length > 0 ? hit.Note.Key : hit.Note.Id, hit.Note.Body, hit.Note.UpdatedAt)).ToArray();
    }
    /// <inheritdoc />
    public void SetFact(string key, string text) { Initialize(); StoreFact(key, text, "explicit_user_memory"); }
    /// <summary>Stores a bounded explicit memory under a stable key while preserving corrections.</summary>
    private void StoreFact(string key, string text, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (key.Length > 100 || text.Length > 4000) { throw new ArgumentException("Memory exceeds supported bounds."); }
        var note = new BrainNote { Key = key.Trim(), Title = key.Trim(), Body = text.Trim(), Source = source, Confidence = 1, Importance = .8,
            Type = "fact", MemoryType = "semantic", Path = "Knowledge/Facts/" + BrainNaming.FileName(key.Trim(), key.Trim()) };
        learning.ConsolidateAsync(note, true).GetAwaiter().GetResult();
    }
    /// <inheritdoc />
    public void DeleteFact(string key)
    {
        Initialize(); BrainNote? note = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && (note.Key == key || note.Id == key));
        if (note is not null) { brain.DeleteAsync(note.Path).GetAwaiter().GetResult(); }
    }
    /// <inheritdoc />
    public void ClearHistory() { Initialize(); transcripts.ClearHistory(); lock (_gate) { _lastUserText = ""; } }
}

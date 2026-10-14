using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Laura.Core.Brain;

/// <summary>Per-installation rebuildable FTS5 and vector cache; note bodies never originate here.</summary>
public sealed class BrainIndex(string directory, IBrainEmbedding embedding)
{
    private readonly object _gate = new();
    private string _path = "";
    private Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private Dictionary<string, BrainPassage> _passages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Hash, IReadOnlyList<BrainPassage> Items)> _layouts = new(StringComparer.Ordinal);
    /// <summary>Gets a privacy-safe indication of optional neural availability.</summary>
    public bool SemanticAvailable { get; private set; }
    /// <summary>Gets observed corrupt-cache recoveries; normal rebuilds and missing optional embeddings are not corruption.</summary>
    public int RecoveryCount { get; private set; }
    /// <summary>Gets the hashed vault identity of the last observed recovery, without persisting a personal directory.</summary>
    public string LastRecoveryVaultIdentity { get; private set; } = "";
    /// <summary>Refreshes derived rows and embeds only changed content or model fingerprints.</summary>
    public void Update(string vault, IReadOnlyList<BrainNote> notes, bool rebuild)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(directory); _path = Path.Combine(directory, "brain-index-" + FileBrainStorage.Hash(vault)[..16] + ".db");
            if (rebuild && File.Exists(_path)) { File.Delete(_path); }
            try { UpdateInternal(notes); }
            catch (SqliteException)
            {
                RecoveryCount++;
                LastRecoveryVaultIdentity = FileBrainStorage.Hash(vault);
                if (File.Exists(_path)) { File.Move(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N"), false); }
                UpdateInternal(notes);
            }
        }
    }
    /// <summary>Builds a transactionally coherent text index and independently fingerprinted vector cache.</summary>
    private void UpdateInternal(IReadOnlyList<BrainNote> notes)
    {
        using var connection = Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA user_version";
            if ((long)schema.ExecuteScalar()! != 2)
            { schema.CommandText = "DROP TABLE IF EXISTS note_fts; DROP TABLE IF EXISTS vectors; PRAGMA user_version=2;"; schema.ExecuteNonQuery(); }
            schema.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS note_fts USING fts5(id UNINDEXED,title,body,tags,hash UNINDEXED,note_id UNINDEXED,start UNINDEXED,length UNINDEXED,heading,tokenize='unicode61 remove_diacritics 2'); CREATE TABLE IF NOT EXISTS vectors(id TEXT PRIMARY KEY,hash TEXT NOT NULL,model TEXT NOT NULL,vector TEXT NOT NULL);";
            schema.ExecuteNonQuery();
        }
        string model = "";
        try { model = embedding.ModelId; } catch (Exception error) when (error is InvalidOperationException or IOException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { }
        SemanticAvailable = model.Length > 0;
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var passages = new Dictionary<string, BrainPassage>(StringComparer.Ordinal);
        using var transaction = connection.BeginTransaction();
        var valid = notes.Where(note => note.IsKnowledge).ToArray();
        var validIds = valid.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string removed in _layouts.Keys.Where(id => !validIds.Contains(id)).ToArray()) { _layouts.Remove(removed); }
        foreach (BrainNote note in valid)
        {
            IReadOnlyList<BrainPassage> spans = _layouts.TryGetValue(note.Id, out var layout) && layout.Hash == note.Hash ? layout.Items : BrainPassages.Split(note);
            _layouts[note.Id] = (note.Hash, spans);
            using var row = connection.CreateCommand(); row.Transaction = transaction;
            row.CommandText = "SELECT hash FROM note_fts WHERE note_id=$note LIMIT 1"; row.Parameters.AddWithValue("$note", note.Id);
            bool unchanged = row.ExecuteScalar() as string == note.Hash;
            if (!unchanged) { row.CommandText = "DELETE FROM note_fts WHERE note_id=$note"; row.ExecuteNonQuery(); }
            foreach (BrainPassage passage in spans)
            {
                passages[passage.Id] = passage;
                string body = note.Body.Substring(passage.Start, passage.Length);
                if (!unchanged)
                {
                    using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO note_fts(id,title,body,tags,hash,note_id,start,length,heading) VALUES($id,$title,$body,$tags,$hash,$note,$start,$length,$heading)";
                    insert.Parameters.AddWithValue("$id", passage.Id); insert.Parameters.AddWithValue("$title", note.Title); insert.Parameters.AddWithValue("$body", body);
                    insert.Parameters.AddWithValue("$tags", string.Join(' ', note.Tags)); insert.Parameters.AddWithValue("$hash", note.Hash); insert.Parameters.AddWithValue("$note", note.Id);
                    insert.Parameters.AddWithValue("$start", passage.Start); insert.Parameters.AddWithValue("$length", passage.Length); insert.Parameters.AddWithValue("$heading", passage.Heading); insert.ExecuteNonQuery();
                }
                if (!SemanticAvailable) { continue; }
                string content = note.Title + "\n" + passage.Heading + "\n" + body; string hash = FileBrainStorage.Hash(content);
                using var existing = connection.CreateCommand(); existing.Transaction = transaction;
                existing.CommandText = "SELECT vector FROM vectors WHERE id=$id AND hash=$hash AND model=$model";
                existing.Parameters.AddWithValue("$id", passage.Id); existing.Parameters.AddWithValue("$hash", hash); existing.Parameters.AddWithValue("$model", model);
                string? cached = existing.ExecuteScalar() as string;
                float[] vector;
                try { vector = cached is not null ? JsonSerializer.Deserialize<float[]>(cached) ?? [] : embedding.Embed(content); }
                catch (Exception error) when (error is JsonException or InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { SemanticAvailable = false; continue; }
                vectors[passage.Id] = vector;
                if (cached is not null) { continue; }
                using var save = connection.CreateCommand(); save.Transaction = transaction;
                save.CommandText = "INSERT INTO vectors(id,hash,model,vector) VALUES($id,$hash,$model,$vector) ON CONFLICT(id) DO UPDATE SET hash=$hash,model=$model,vector=$vector";
                save.Parameters.AddWithValue("$id", passage.Id); save.Parameters.AddWithValue("$hash", hash); save.Parameters.AddWithValue("$model", model); save.Parameters.AddWithValue("$vector", JsonSerializer.Serialize(vector)); save.ExecuteNonQuery();
            }
        }
        var identities = passages.Keys.ToHashSet(StringComparer.Ordinal);
        using (var all = connection.CreateCommand())
        {
            all.Transaction = transaction; all.CommandText = "SELECT id FROM vectors UNION SELECT id FROM note_fts"; var stale = new List<string>();
            using (var reader = all.ExecuteReader()) { while (reader.Read()) { if (!identities.Contains(reader.GetString(0))) { stale.Add(reader.GetString(0)); } } }
            foreach (string id in stale) { using var delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM vectors WHERE id=$id; DELETE FROM note_fts WHERE id=$id"; delete.Parameters.AddWithValue("$id", id); delete.ExecuteNonQuery(); }
        }
        transaction.Commit(); _vectors = vectors; _passages = passages;
        var noteIds = valid.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _layouts.Keys.Where(id => !noteIds.Contains(id)).ToArray()) { _layouts.Remove(id); }
    }
    /// <summary>Retrieves lexical candidates using quoted tokens, never user-supplied SQL/FTS operators.</summary>
    public IReadOnlyDictionary<string, double> Lexical(string query)
        => Query(query).Lexical;
    /// <summary>Returns lexical and local semantic candidates with their real source locations in one coherent index view.</summary>
    public BrainCandidates Query(string query)
    {
        lock (_gate)
        {
            var lexical = new Dictionary<string, double>(StringComparer.Ordinal); var selected = new Dictionary<string, BrainPassage>(StringComparer.Ordinal);
            string[] words = Terms(query);
            if (_path.Length == 0 || words.Length == 0) { return new(lexical, new Dictionary<string, double>(), selected, words); }
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                WITH matches AS MATERIALIZED (
                    SELECT id,note_id,bm25(note_fts,0,3,1,2,0,0,0,0,1) AS score
                    FROM note_fts WHERE note_fts MATCH $query ORDER BY score,id LIMIT 4096
                )
                SELECT id,note_id FROM (
                    SELECT *,ROW_NUMBER() OVER(PARTITION BY note_id ORDER BY score,id) AS ordinal FROM matches
                ) WHERE ordinal=1 ORDER BY score,note_id LIMIT 80
                """;
            command.Parameters.AddWithValue("$query", string.Join(" OR ", words.Select(word => "\"" + word + "\"")));
            using (var reader = command.ExecuteReader())
            {
                int rank = 0;
                while (reader.Read()) { string id = reader.GetString(1); lexical[id] = 1d / (1 + rank++ * .12); if (_passages.TryGetValue(reader.GetString(0), out BrainPassage? passage)) { selected[id] = passage; } }
            }
            var semantic = SemanticDetails(query);
            foreach (var item in semantic)
            { if (!selected.ContainsKey(item.NoteId)) { selected[item.NoteId] = item.Passage; } }
            return new(lexical, semantic.ToDictionary(item => item.NoteId, item => item.Score, StringComparer.Ordinal), selected, words);
        }
    }
    /// <summary>Tokenizes literal query terms with bounds; punctuation and FTS operators have no authority.</summary>
    public static string[] Terms(string query) => System.Text.RegularExpressions.Regex.Matches(query, @"[\p{L}\p{N}_]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        .Select(match => match.Value).Where(word => word.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
    /// <summary>Returns cosine candidates from normalized local model vectors.</summary>
    public IReadOnlyDictionary<string, double> Semantic(string query)
    {
        lock (_gate)
        {
            return SemanticDetails(query).ToDictionary(item => item.NoteId, item => item.Score, StringComparer.Ordinal);
        }
    }
    private (string NoteId, double Score, BrainPassage Passage)[] SemanticDetails(string query)
    {
        if (!SemanticAvailable || string.IsNullOrWhiteSpace(query)) { return []; }
        float[] vector;
        try { vector = embedding.Embed(query); } catch (Exception error) when (error is InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException) { return []; }
        return _vectors.Where(pair => pair.Value.Length == vector.Length && vector.Length > 0 && _passages.ContainsKey(pair.Key))
            .Select(pair => (Passage: _passages[pair.Key], Score: pair.Value.Zip(vector, (left, right) => (double)left * right).Sum()))
            .Where(pair => pair.Score >= .35).OrderByDescending(pair => pair.Score).GroupBy(pair => pair.Passage.NoteId, StringComparer.Ordinal)
            .Select(group => (NoteId: group.Key, Score: group.First().Score, Passage: group.First().Passage)).Take(60).ToArray();
    }
    /// <summary>Opens only a derived local database with no cross-device pooling.</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { connection.Open(); return connection; } catch { connection.Dispose(); throw; }
    }
}

/// <summary>First-stage bounded candidates; source spans are derived and never mutate knowledge or graph edges.</summary>
public sealed record BrainCandidates(IReadOnlyDictionary<string, double> Lexical, IReadOnlyDictionary<string, double> Semantic, IReadOnlyDictionary<string, BrainPassage> Passages, IReadOnlyList<string> Terms);

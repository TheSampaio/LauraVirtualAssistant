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
    /// <summary>Gets a privacy-safe indication of optional neural availability.</summary>
    public bool SemanticAvailable { get; private set; }
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
            schema.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS note_fts USING fts5(id UNINDEXED,title,body,tags,hash UNINDEXED,tokenize='unicode61 remove_diacritics 2'); CREATE TABLE IF NOT EXISTS vectors(id TEXT PRIMARY KEY,hash TEXT NOT NULL,model TEXT NOT NULL,vector TEXT NOT NULL);";
            schema.ExecuteNonQuery();
        }
        string model = "";
        try { model = embedding.ModelId; } catch (Exception error) when (error is InvalidOperationException or IOException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { }
        SemanticAvailable = model.Length > 0;
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        using var transaction = connection.BeginTransaction();
        var valid = notes.Where(note => note.IsKnowledge).ToArray();
        foreach (BrainNote note in valid)
        {
            using var row = connection.CreateCommand(); row.Transaction = transaction;
            row.CommandText = "SELECT hash FROM note_fts WHERE id=$id"; row.Parameters.AddWithValue("$id", note.Id);
            bool unchanged = row.ExecuteScalar() as string == note.Hash;
            if (!unchanged)
            {
            row.CommandText = "DELETE FROM note_fts WHERE id=$id; INSERT INTO note_fts(id,title,body,tags,hash) VALUES($id,$title,$body,$tags,$hash)";
            row.Parameters.AddWithValue("$title", note.Title); row.Parameters.AddWithValue("$body", note.Body); row.Parameters.AddWithValue("$tags", string.Join(' ', note.Tags)); row.Parameters.AddWithValue("$hash", note.Hash); row.ExecuteNonQuery();
            }
            if (!SemanticAvailable) { continue; }
            string content = note.Title + "\n" + note.Body; string hash = FileBrainStorage.Hash(content);
            using var existing = connection.CreateCommand(); existing.Transaction = transaction;
            existing.CommandText = "SELECT vector FROM vectors WHERE id=$id AND hash=$hash AND model=$model";
            existing.Parameters.AddWithValue("$id", note.Id); existing.Parameters.AddWithValue("$hash", hash); existing.Parameters.AddWithValue("$model", model);
            string? cached = existing.ExecuteScalar() as string;
            float[] vector;
            try { vector = cached is not null ? JsonSerializer.Deserialize<float[]>(cached) ?? [] : embedding.Embed(content); }
            catch (Exception error) when (error is JsonException or InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { SemanticAvailable = false; continue; }
            vectors[note.Id] = vector;
            using var save = connection.CreateCommand(); save.Transaction = transaction;
            save.CommandText = "INSERT INTO vectors(id,hash,model,vector) VALUES($id,$hash,$model,$vector) ON CONFLICT(id) DO UPDATE SET hash=$hash,model=$model,vector=$vector";
            save.Parameters.AddWithValue("$id", note.Id); save.Parameters.AddWithValue("$hash", hash); save.Parameters.AddWithValue("$model", model); save.Parameters.AddWithValue("$vector", JsonSerializer.Serialize(vector)); save.ExecuteNonQuery();
        }
        var identities = valid.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
        using (var all = connection.CreateCommand())
        {
            all.Transaction = transaction; all.CommandText = "SELECT id FROM vectors UNION SELECT id FROM note_fts"; var stale = new List<string>();
            using (var reader = all.ExecuteReader()) { while (reader.Read()) { if (!identities.Contains(reader.GetString(0))) { stale.Add(reader.GetString(0)); } } }
            foreach (string id in stale) { using var delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM vectors WHERE id=$id; DELETE FROM note_fts WHERE id=$id"; delete.Parameters.AddWithValue("$id", id); delete.ExecuteNonQuery(); }
        }
        transaction.Commit(); _vectors = vectors;
    }
    /// <summary>Retrieves lexical candidates using quoted tokens, never user-supplied SQL/FTS operators.</summary>
    public IReadOnlyDictionary<string, double> Lexical(string query)
    {
        lock (_gate)
        {
            if (_path.Length == 0 || string.IsNullOrWhiteSpace(query)) { return new Dictionary<string, double>(); }
            string[] words = System.Text.RegularExpressions.Regex.Matches(query, @"[\p{L}\p{N}_]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                .Select(match => match.Value).Where(word => word.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
            if (words.Length == 0) { return new Dictionary<string, double>(); }
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,bm25(note_fts,0,3,1,2) AS score FROM note_fts WHERE note_fts MATCH $query ORDER BY score LIMIT 80";
            command.Parameters.AddWithValue("$query", string.Join(" OR ", words.Select(word => "\"" + word + "\"")));
            using var reader = command.ExecuteReader(); var result = new Dictionary<string, double>(StringComparer.Ordinal); int rank = 0;
            while (reader.Read()) { result[reader.GetString(0)] = 1d / (1 + rank++ * .12); }
            return result;
        }
    }
    /// <summary>Returns cosine candidates from normalized local model vectors.</summary>
    public IReadOnlyDictionary<string, double> Semantic(string query)
    {
        lock (_gate)
        {
            if (!SemanticAvailable || string.IsNullOrWhiteSpace(query)) { return new Dictionary<string, double>(); }
            float[] vector;
            try { vector = embedding.Embed(query); } catch (Exception error) when (error is InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException) { return new Dictionary<string, double>(); }
            return _vectors.Where(pair => pair.Value.Length == vector.Length && vector.Length > 0)
                .Select(pair => (pair.Key, Score: pair.Value.Zip(vector, (left, right) => (double)left * right).Sum()))
                .Where(pair => pair.Score >= .35).OrderByDescending(pair => pair.Score).Take(60).ToDictionary(pair => pair.Key, pair => pair.Score, StringComparer.Ordinal);
        }
    }
    /// <summary>Opens only a derived local database with no cross-device pooling.</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { connection.Open(); return connection; } catch { connection.Dispose(); throw; }
    }
}

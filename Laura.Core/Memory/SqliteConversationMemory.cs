using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Microsoft.Data.Sqlite;

namespace Laura.Core.Memory;

/// <summary>Parameterized SQLite storage; transactions preserve existing data on failure.</summary>
public sealed partial class SqliteConversationMemory : IConversationMemory
{
    private readonly string _path;
    private readonly object _gate = new();
    private bool _initialized;

    /// <summary>Targets a database in Laura-owned user storage without performing startup I/O.</summary>
    public SqliteConversationMemory(string path) => _path = Path.GetFullPath(path);

    /// <inheritdoc />
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) { return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using SqliteConnection connection = Open();
            using SqliteCommand version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            long schemaVersion = (long)version.ExecuteScalar()!;
            if (schemaVersion > 2) { throw new InvalidOperationException("Memory schema is newer than this Laura version."); }
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = """
                CREATE TABLE IF NOT EXISTS messages (id INTEGER PRIMARY KEY, source INTEGER NOT NULL, text TEXT NOT NULL, stamp TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS facts (key TEXT PRIMARY KEY, text TEXT NOT NULL, stamp TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS learning_events (message_id INTEGER PRIMARY KEY,vault TEXT NOT NULL);
                PRAGMA user_version=2;
                """;
            migration.ExecuteNonQuery();
            transaction.Commit();
            _initialized = true;
        }
    }

    /// <summary>Opens a short-lived connection; SQLite handles synchronization between readers and writers.</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _path, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    /// <inheritdoc />
    public void Append(ConversationMessage message) => AppendCore(message, "");
    /// <summary>Atomically retains a user message and a learning receipt referencing its existing transcript row, without duplicating text.</summary>
    public void AppendForLearning(ConversationMessage message, string vaultIdentity)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Source == ConversationMessageSource.Assistant || vaultIdentity.Length != 64 || !vaultIdentity.All(Uri.IsHexDigit))
        { throw new ArgumentException("Learning receipts require a user source and a hashed vault identity."); }
        AppendCore(message, vaultIdentity);
    }
    private void AppendCore(ConversationMessage message, string vaultIdentity)
    {
        ArgumentNullException.ThrowIfNull(message);
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO messages(source,text,stamp) VALUES($source,$text,$stamp);
            INSERT INTO learning_events(message_id,vault) SELECT last_insert_rowid(),$vault WHERE length($vault)>0;
            DELETE FROM messages WHERE id NOT IN (SELECT id FROM messages ORDER BY id DESC LIMIT 2000);
            DELETE FROM learning_events WHERE message_id NOT IN (SELECT id FROM messages);
            """;
        command.Parameters.AddWithValue("$source", (int)message.Source);
        command.Parameters.AddWithValue("$text", message.Text.Length > 16000 ? message.Text[..16000] : message.Text);
        command.Parameters.AddWithValue("$stamp", message.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$vault", vaultIdentity);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<ConversationMessage> ReadRecent(int limit)
    {
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT source,text,stamp FROM (SELECT * FROM messages ORDER BY id DESC LIMIT $limit) ORDER BY id";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 0, 200));
        using SqliteDataReader reader = command.ExecuteReader();
        var messages = new List<ConversationMessage>();
        while (reader.Read())
        {
            messages.Add(new((ConversationMessageSource)reader.GetInt32(0), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        }
        return messages;
    }

    /// <inheritdoc />
    public IReadOnlyList<MemoryFact> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT key,text,stamp FROM facts WHERE instr(lower(key || ' ' || text), lower($query)) > 0 ORDER BY stamp DESC LIMIT 100";
        command.Parameters.AddWithValue("$query", query);
        using SqliteDataReader reader = command.ExecuteReader();
        var facts = new List<MemoryFact>();
        while (reader.Read()) { facts.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture))); }
        return facts;
    }

    /// <summary>Reads all legacy facts for a lossless one-time Markdown migration, without modifying their source rows.</summary>
    public IReadOnlyList<MemoryFact> ReadAllFacts()
    {
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT key,text,stamp FROM facts ORDER BY key";
        using SqliteDataReader reader = command.ExecuteReader();
        var facts = new List<MemoryFact>();
        while (reader.Read()) { facts.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture))); }
        return facts;
    }

    /// <inheritdoc />
    public void SetFact(string key, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (key.Length > 100 || text.Length > 2000) { throw new ArgumentException("Memory is too long."); }
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO facts(key,text,stamp) VALUES($key,$text,$stamp) ON CONFLICT(key) DO UPDATE SET text=$text,stamp=$stamp";
        command.Parameters.AddWithValue("$key", key.Trim());
        command.Parameters.AddWithValue("$text", text.Trim());
        command.Parameters.AddWithValue("$stamp", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void DeleteFact(string key)
    {
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM facts WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        Initialize();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "BEGIN; DELETE FROM learning_events; DELETE FROM messages; COMMIT; VACUUM;";
        command.ExecuteNonQuery();
    }
}

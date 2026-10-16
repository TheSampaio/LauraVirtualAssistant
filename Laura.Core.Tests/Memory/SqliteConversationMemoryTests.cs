using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Data.Sqlite;

namespace Laura.Core.Tests.Memory;

public sealed class SqliteConversationMemoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-memory-tests-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_directory, "memory.db");

    [Fact]
    public void PersistsHistoryAndFactsAcrossStoreInstances()
    {
        var store = new SqliteConversationMemory(Database);
        var message = new ConversationMessage(ConversationMessageSource.UserTyped, "Remember my preference", DateTimeOffset.UtcNow);
        store.Append(message);
        store.SetFact("language", "Brazilian Portuguese");
        var reopened = new SqliteConversationMemory(Database);
        Assert.Equal(message, Assert.Single(reopened.ReadRecent(10)));
        Assert.Equal("language", Assert.Single(reopened.Search("Portuguese")).Key);
    }

    [Fact]
    public void MigratesVersionFourAndPreservesNewTurnIdentitiesAcrossRestart()
    {
        Directory.CreateDirectory(_directory);
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE messages(id INTEGER PRIMARY KEY,source INTEGER NOT NULL,text TEXT NOT NULL,stamp TEXT NOT NULL); INSERT INTO messages VALUES(1,0,'Legacy question','2026-10-15T00:00:00Z'); PRAGMA user_version=4";
            command.ExecuteNonQuery();
        }
        var store = new SqliteConversationMemory(Database); Guid turnId = Guid.NewGuid();
        store.Append(new(ConversationMessageSource.UserTyped, "New question", DateTimeOffset.UtcNow) { TurnId = turnId });
        var messages = new SqliteConversationMemory(Database).ReadRecent(10);
        Assert.Equal(Guid.Empty, messages[0].TurnId); Assert.Equal("Legacy question", messages[0].Text);
        Assert.Equal(turnId, messages[1].TurnId); Assert.Equal("New question", messages[1].Text);
    }
    [Fact]
    public void LiteralSearchAndDeletionCannotExecuteInjectedSql()
    {
        var store = new SqliteConversationMemory(Database);
        store.SetFact("safe", "Keep this");
        store.SetFact("' OR 1=1;--", "Delete only this");
        store.DeleteFact("' OR 1=1;--");
        Assert.Equal("safe", Assert.Single(store.Search("")).Key);
        Assert.Empty(store.Search("%"));
        Assert.Empty(store.Search("' OR 1=1;--"));
    }

    [Fact]
    public void HistoryDeletionPreservesExplicitMemories()
    {
        var store = new SqliteConversationMemory(Database);
        store.Append(new(ConversationMessageSource.Assistant, "Hello", DateTimeOffset.UtcNow));
        store.SetFact("name", "Alex");
        store.ClearHistory();
        Assert.Empty(store.ReadRecent(10));
        Assert.Single(store.Search("Alex"));
    }

    [Fact]
    public void RejectsFutureSchemaWithoutReplacingDatabase()
    {
        Directory.CreateDirectory(_directory);
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => new SqliteConversationMemory(Database).Initialize());
        Assert.True(File.Exists(Database));
    }
    [Fact]
    public void MigratesVersionOneWithoutInventingReceiptsForHistoricalText()
    {
        var original = new SqliteConversationMemory(Database); original.Append(new(ConversationMessageSource.UserTyped, "Synthetic old history", DateTimeOffset.UtcNow)); original.SetFact("synthetic", "Existing fact");
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE learning_events; PRAGMA user_version=1;"; command.ExecuteNonQuery();
        }
        var migrated = new SqliteConversationMemory(Database); Assert.Single(migrated.ReadRecent(10)); Assert.Single(migrated.Search("Existing"));
        Assert.Empty(migrated.ReadPendingLearning(new string('A', 64)));
    }
    [Fact]
    public void MigratesVersionThreeAndBoundsSkillEvidenceWithoutDeletingFactsOrMessages()
    {
        var original = new SqliteConversationMemory(Database); original.Append(new(ConversationMessageSource.UserTyped, "Preserve synthetic history", DateTimeOffset.UtcNow)); original.SetFact("fixture", "Preserved explicit fact");
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE skill_outcomes; PRAGMA user_version=3;"; command.ExecuteNonQuery();
        }
        var migrated = new SqliteConversationMemory(Database); string vault = new('A', 64), skill = Guid.NewGuid().ToString("D"), hash = new('B', 64);
        Assert.Empty(migrated.ReadSkillOutcomes(vault, skill)); Assert.Single(migrated.ReadRecent(10)); Assert.Single(migrated.Search("Preserved"));
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<1005) INSERT INTO skill_outcomes(id,vault,skill,hash,tool,exit_code,operation,stamp) SELECT hex(randomblob(16)),$vault,$skill,$hash,'run_program',1,$hash,'2026-10-14T00:00:00Z' FROM n";
            command.Parameters.AddWithValue("$vault", vault); command.Parameters.AddWithValue("$skill", skill); command.Parameters.AddWithValue("$hash", hash); command.ExecuteNonQuery();
        }
        migrated.RecordSkillOutcome(vault, new(Guid.NewGuid(), skill, hash, "run_program", 0, hash, DateTimeOffset.UtcNow));
        Assert.Equal(50, migrated.ReadSkillOutcomes(vault, skill).Count); Assert.Empty(migrated.ReadSkillOutcomes(new string('C', 64), skill));
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM skill_outcomes"; Assert.Equal(1000L, command.ExecuteScalar()); }
        migrated.ClearHistory(); Assert.Equal(50, migrated.ReadSkillOutcomes(vault, skill).Count); Assert.Empty(migrated.ReadRecent(10)); Assert.Single(migrated.Search("Preserved"));
    }
    [Fact]
    public void TranscriptRetentionAlsoRemovesReceiptsForExpiredMessages()
    {
        var store = new SqliteConversationMemory(Database); string identity = new('A', 64);
        store.AppendForLearning(new(ConversationMessageSource.UserTyped, "Synthetic pending oldest event", DateTimeOffset.UtcNow), identity);
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<2000) INSERT INTO messages(source,text,stamp) SELECT 0,'Synthetic retained filler','2026-10-13T00:00:00Z' FROM n;"; command.ExecuteNonQuery();
        }
        store.Append(new(ConversationMessageSource.Assistant, "Synthetic final retained text", DateTimeOffset.UtcNow));
        Assert.Empty(store.ReadPendingLearning(identity));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }
}

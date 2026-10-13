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

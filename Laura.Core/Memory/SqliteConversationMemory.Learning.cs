namespace Laura.Core.Memory;

/// <summary>A selected pending user declaration read from the existing transcript, scoped to a hashed vault identity.</summary>
public sealed record PendingBrainLearning(long Id, string Text);

public sealed partial class SqliteConversationMemory
{
    /// <summary>Reads a bounded batch of selected receipts; arbitrary historical or generated messages are never replayed.</summary>
    public IReadOnlyList<PendingBrainLearning> ReadPendingLearning(string vaultIdentity)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT e.message_id,m.text FROM learning_events e JOIN messages m ON m.id=e.message_id WHERE e.vault=$vault ORDER BY e.message_id LIMIT 2000";
        command.Parameters.AddWithValue("$vault", vaultIdentity); using var reader = command.ExecuteReader();
        var result = new List<PendingBrainLearning>(); while (reader.Read()) { result.Add(new(reader.GetInt64(0), reader.GetString(1))); } return result;
    }
    /// <summary>Checks whether a receipt still exists after history deletion or retention.</summary>
    public bool HasPendingLearning(long id, string vaultIdentity)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM learning_events WHERE message_id=$id AND vault=$vault";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$vault", vaultIdentity); return (long)command.ExecuteScalar()! == 1;
    }
    /// <summary>Acknowledges only the selected receipt after Markdown consolidation completes; interrupted acknowledgments are safe to retry.</summary>
    public void CompleteLearning(long id, string vaultIdentity)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM learning_events WHERE message_id=$id AND vault=$vault";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$vault", vaultIdentity); command.ExecuteNonQuery();
    }
}

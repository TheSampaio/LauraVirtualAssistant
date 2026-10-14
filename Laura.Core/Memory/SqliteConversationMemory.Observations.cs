using Laura.Core.Brain;

namespace Laura.Core.Memory;

public sealed partial class SqliteConversationMemory
{
    /// <summary>Retains a bounded execution receipt in the existing local database until knowledge consolidation completes.</summary>
    public void RetainObservation(Guid id, string vaultIdentity, string payload)
    {
        if (id == Guid.Empty || vaultIdentity.Length != 64 || !vaultIdentity.All(Uri.IsHexDigit) || payload.Length is 0 or > 16000) { throw new ArgumentException("Invalid bounded observation receipt."); }
        Initialize(); using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM execution_events WHERE id<>$id"; command.Parameters.AddWithValue("$id", id.ToString("D"));
        if ((long)command.ExecuteScalar()! >= 200) { throw new IOException("Execution learning receipt capacity reached; no receipt was silently discarded."); }
        command.CommandText = "INSERT INTO execution_events(id,vault,payload) VALUES($id,$vault,$payload) ON CONFLICT(id) DO NOTHING";
        command.Parameters.AddWithValue("$vault", vaultIdentity); command.Parameters.AddWithValue("$payload", payload); command.ExecuteNonQuery(); transaction.Commit();
    }
    /// <summary>Reads only selected observations for the current vault, with a hard receipt bound.</summary>
    public IReadOnlyList<PendingBrainObservation> ReadObservations(string vaultIdentity)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,payload FROM execution_events WHERE vault=$vault ORDER BY rowid LIMIT 200"; command.Parameters.AddWithValue("$vault", vaultIdentity);
        using var reader = command.ExecuteReader(); var result = new List<PendingBrainObservation>();
        while (reader.Read()) { if (Guid.TryParse(reader.GetString(0), out Guid id)) { result.Add(new(id, reader.GetString(1))); } } return result;
    }
    /// <summary>Acknowledges a single scoped observation only after Markdown persistence.</summary>
    public void CompleteObservation(Guid id, string vaultIdentity)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM execution_events WHERE id=$id AND vault=$vault"; command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$vault", vaultIdentity); command.ExecuteNonQuery();
    }
}

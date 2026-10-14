using System.Globalization;
using Laura.Core.Brain;

namespace Laura.Core.Memory;

public sealed partial class SqliteConversationMemory
{
    /// <summary>Idempotently retains at most 1,000 native skill outcomes globally. No input, arguments, output or user text is retained here.</summary>
    public void RecordSkillOutcome(string vaultIdentity, BrainSkillOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!Hash(vaultIdentity) || outcome.Id == Guid.Empty || !Guid.TryParse(outcome.SkillId, out _) || !Hash(outcome.SkillHash) || !Hash(outcome.OperationHash) || outcome.Tool != "run_program")
        { throw new ArgumentException("Invalid bounded skill observation."); }
        Initialize(); using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO skill_outcomes(id,vault,skill,hash,tool,exit_code,operation,stamp) VALUES($id,$vault,$skill,$hash,$tool,$exit,$operation,$stamp) ON CONFLICT(id) DO NOTHING;
            DELETE FROM skill_outcomes WHERE rowid NOT IN (SELECT rowid FROM skill_outcomes ORDER BY rowid DESC LIMIT 1000);
            """;
        command.Parameters.AddWithValue("$id", outcome.Id.ToString("D")); command.Parameters.AddWithValue("$vault", vaultIdentity);
        command.Parameters.AddWithValue("$skill", outcome.SkillId); command.Parameters.AddWithValue("$hash", outcome.SkillHash); command.Parameters.AddWithValue("$tool", outcome.Tool);
        command.Parameters.AddWithValue("$exit", outcome.ExitCode); command.Parameters.AddWithValue("$operation", outcome.OperationHash);
        command.Parameters.AddWithValue("$stamp", outcome.Timestamp.ToString("O", CultureInfo.InvariantCulture)); command.ExecuteNonQuery(); transaction.Commit();
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
    /// <summary>Reads at most 50 recent outcomes in the selected vault and version, without exposing transcripts or unbounded history.</summary>
    public IReadOnlyList<BrainSkillOutcome> ReadSkillOutcomes(string vaultIdentity, string skillId, string hash = "")
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,skill,hash,tool,exit_code,operation,stamp FROM skill_outcomes WHERE vault=$vault AND skill=$skill AND ($hash='' OR hash=$hash) ORDER BY rowid DESC LIMIT 50";
        command.Parameters.AddWithValue("$vault", vaultIdentity); command.Parameters.AddWithValue("$skill", skillId); command.Parameters.AddWithValue("$hash", hash);
        using var reader = command.ExecuteReader(); var result = new List<BrainSkillOutcome>();
        while (reader.Read()) { result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture))); }
        return result;
    }
}

using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>One completed native tool observation for one reviewed skill version; exit status does not establish procedure or goal success.</summary>
public sealed record BrainSkillOutcome(Guid Id, string SkillId, string SkillHash, string Tool, int ExitCode, string OperationHash, DateTimeOffset Timestamp);

/// <summary>A transient association captured by the native adapter after reading reviewed instructions. Never accepted from model arguments.</summary>
public sealed record BrainSkillUse(Guid TurnId, string SkillId, string SkillHash, string Vault, DateTimeOffset LoadedAt);

/// <summary>Attributes completed process observations to explicitly loaded, still approved procedures in the same user turn. Does not execute, activate or rewrite skills.</summary>
public sealed class BrainSkillUsage(BrainService brain, BrainSkills skills, ISettingsService settings, SqliteConversationMemory journal, ILogger<BrainSkillUsage> logger)
{
    private readonly object _gate = new();
    private BrainSkillUse? _loaded;
    /// <summary>Records only a transient binding after actual instruction loading; loading alone produces no usage outcome.</summary>
    public bool Bind(BrainNote note, ConversationWorkingContext context)
    {
        lock (_gate)
        {
            _loaded = Enabled && context.TurnId != Guid.Empty && context.UserText.Length > 0 && !BrainLearning.Sensitive(context.UserText) && skills.IsEnabled(note)
                ? new(context.TurnId, note.Id, note.Hash, brain.Location, DateTimeOffset.UtcNow) : null;
            return _loaded is not null;
        }
    }
    /// <summary>Freezes the native association before queuing a tool; another turn cannot inherit it.</summary>
    public BrainSkillUse? Capture(Guid turnId)
    { lock (_gate) { return _loaded?.TurnId == turnId && turnId != Guid.Empty ? _loaded : null; } }
    private bool Enabled => settings.Current.RememberConversations && settings.Current.Brain.AutoLearn;

    /// <summary>Persists privacy-minimized evidence only after real completion, rechecking turn, vault, exact version, approval and declared tool. Recording failure never changes the tool result.</summary>
    public async Task<bool> ObserveAsync(BrainSkillUse? use, BrainExecutionObservation observation, Func<Guid> currentTurn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(currentTurn);
        if (use is null || !Enabled || use.TurnId != currentTurn() || use.TurnId == Guid.Empty || brain.Location != use.Vault
            || DateTimeOffset.UtcNow - use.LoadedAt is var age && (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(10))
            || observation.Timestamp < use.LoadedAt || observation.Timestamp > DateTimeOffset.UtcNow.AddSeconds(5)) { return false; }
        try
        {
            await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            BrainNote? note = brain.Snapshot.Notes.SingleOrDefault(note => note.Id == use.SkillId && note.Hash == use.SkillHash && note.Type == "skill");
            if (!Enabled || use.TurnId != currentTurn() || brain.Location != use.Vault || note is null || !skills.IsEnabled(note) || note.Procedure?.Tools.Contains("run_program", StringComparer.Ordinal) != true) { return false; }
            journal.RecordSkillOutcome(FileBrainStorage.Hash(use.Vault), new(observation.Id, use.SkillId, use.SkillHash, "run_program", observation.ExitCode, observation.OperationHash, observation.Timestamp));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { logger.LogWarning("Skill outcome recording unavailable ({ErrorType}); observed execution remains independent.", error.GetType().Name); return false; }
    }
    /// <summary>Returns retained evidence for a real skill, separated by exact version. Does not infer goal success, diagnose cause or change approval.</summary>
    public IReadOnlyList<BrainSkillOutcome> Read(string skillId, string hash = "") => journal.ReadSkillOutcomes(FileBrainStorage.Hash(brain.Location), skillId, hash);
}

using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Memory;
using Laura.Core.Text;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>Consolidates selected native execution observations into bounded episodes. It never executes actions or treats process exit as verified goal completion.</summary>
public sealed class BrainExperiences(BrainService brain, ISettingsService settings, SqliteConversationMemory journal, ILogger<BrainExperiences> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Persists only a privacy-filtered actual process outcome, then consolidates durably. Failures preserve the observed tool result.</summary>
    public async Task<BrainMemoryResult> ObserveAsync(BrainExecutionObservation observation, string userGoal, string expectedVault, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation); ArgumentNullException.ThrowIfNull(userGoal);
        if (!Eligible(userGoal) || !Valid(observation)) { return new("excluded", Reason: "Observation disabled, sensitive or invalid."); }
        try
        {
            journal.RetainObservation(observation.Id, FileBrainStorage.Hash(expectedVault), JsonSerializer.Serialize(new BrainObservationReceipt(observation, userGoal)));
            await RecoverAsync(cancellationToken).ConfigureAwait(false);
            bool retained = journal.ReadObservations(FileBrainStorage.Hash(expectedVault)).Any(item => item.Id == observation.Id);
            string marker = "[Observation " + observation.Id.ToString("D") + "]";
            BrainNote? recorded = brain.Snapshot.Notes.FirstOrDefault(note => note.Type == "experience" && note.IsKnowledge && note.Body.Contains(marker, StringComparison.Ordinal));
            return new(retained ? "pending" : recorded is not null ? "recorded" : "not_recorded", recorded?.Id ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { logger.LogWarning("Execution learning unavailable ({ErrorType}); execution result remains independent.", error.GetType().Name); return new("unavailable"); }
    }
    private bool Eligible(string goal) => settings.Current.RememberConversations && settings.Current.Brain.AutoLearn && goal.Length is >= 8 and <= 2000 && !BrainLearning.Sensitive(goal);
    private static bool Valid(BrainExecutionObservation observation) => observation.Id != Guid.Empty && observation.Subject is { Length: > 0 and <= 120 }
        && !observation.Subject.Contains(Path.DirectorySeparatorChar) && !observation.Subject.Contains(Path.AltDirectorySeparatorChar)
        && !BrainLearning.Sensitive(observation.Subject) && observation.OperationHash is { Length: 64 } && observation.OperationHash.All(Uri.IsHexDigit);
    /// <summary>Recovers selected completed observations as knowledge only. It cannot re-run a command after restart.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn) { return; }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string vault = brain.Location, identity = FileBrainStorage.Hash(vault); await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
            foreach (PendingBrainObservation pending in journal.ReadObservations(identity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    BrainObservationReceipt? receipt = JsonSerializer.Deserialize<BrainObservationReceipt>(pending.Payload);
                    if (receipt is null || receipt.Observation is null || receipt.UserGoal is null || receipt.Observation.Id != pending.Id || !Valid(receipt.Observation) || !Eligible(receipt.UserGoal))
                    { logger.LogWarning("Invalid or excluded execution observation retained for review."); continue; }
                    await ConsolidateAsync(receipt, vault, cancellationToken).ConfigureAwait(false); journal.CompleteObservation(pending.Id, identity);
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
                { logger.LogWarning("Execution observation retained for review ({ErrorType}).", error.GetType().Name); }
            }
        }
        finally { _gate.Release(); }
    }
    private async Task ConsolidateAsync(BrainObservationReceipt receipt, string vault, CancellationToken cancellationToken)
    {
        string key = "experience." + FileBrainStorage.Hash(TextNormalizer.Normalize(receipt.UserGoal))[..20]; string marker = "[Observation " + receipt.Observation.Id.ToString("D") + "]";
        if (brain.Snapshot.Notes.Any(note => note.Key == key && note.Type == "experience" && note.Body.Contains(marker, StringComparison.Ordinal))) { return; }
        BrainNote? previous = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Type == "experience" && note.Key == key);
        string observation = marker + "\nTime: " + receipt.Observation.Timestamp.ToString("O", CultureInfo.InvariantCulture) + "\nTool: run_program\nProgram: " + receipt.Observation.Subject
            + "\nObserved exit code: " + receipt.Observation.ExitCode.ToString(CultureInfo.InvariantCulture) + "\nOperation fingerprint: " + receipt.Observation.OperationHash
            + "\nGoal completion: not independently verified.\n";
        string body = previous?.Body ?? "# Operational experience\n\nUser goal (context, not a verified outcome):\n" + receipt.UserGoal + "\n\n";
        if (body.Length > 10000) { body = "# Operational experience\n\nEarlier observations remain in authored revisions.\n\n"; }
        var note = previous ?? new BrainNote { Path = "Memories/Experiences/" + BrainNaming.FileName(receipt.UserGoal, key), Key = key,
            Title = receipt.UserGoal[..Math.Min(90, receipt.UserGoal.Length)], Type = "experience", MemoryType = "episodic", Source = "native_execution_observation", Confidence = 1, Importance = .65, Tags = ["execution", "experience"] };
        await brain.SaveAtLocationAsync(note with { Body = body.TrimEnd() + "\n\n" + observation, Verification = "observed_execution" }, vault, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Proposes one bounded lesson per source version for review; reflection never becomes verified knowledge automatically.</summary>
    public async Task<BrainMemoryResult> ProposeReflectionAsync(string noteId, string expectedHash, string lesson, string evidence, CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn || lesson.Length is < 8 or > 1000 || evidence.Length is < 8 or > 1500
            || BrainLearning.Sensitive(lesson + " " + evidence)) { return new("rejected", Reason: "Disabled, sensitive or unbounded reflection."); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string vault = brain.Location; await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            BrainNote? source = brain.Snapshot.Notes.SingleOrDefault(note => note.Id == noteId && note.Hash == expectedHash && note.IsKnowledge && note.Type == "experience" && note.Verification == "observed_execution");
            if (source is null || !source.Body.Contains(evidence, StringComparison.Ordinal)) { return new("rejected", Reason: "Missing current execution evidence."); }
            string key = "reflection." + source.Id + "." + source.Hash;
            BrainNote? prior = brain.Snapshot.Notes.FirstOrDefault(note => note.Key == key); if (prior is not null) { return new("repeated", prior.Id); }
            BrainNote proposal = await brain.SaveAtLocationAsync(new() { Path = "Inbox/Reflections/" + BrainNaming.FileName(source.Title, key), Key = key,
                Title = "Reflection: " + source.Title, Type = "hypothesis", MemoryType = "semantic", Status = "draft", Verification = "unverified", Confidence = .4,
                Source = "bounded_reflection", Body = "# Proposed lesson\n\n" + lesson + "\n\n## Observed basis\n\n" + evidence,
                Relations = [new(source.Id, "learned_from", .6, .4, evidence, "bounded_reflection")] }, vault, cancellationToken).ConfigureAwait(false);
            return new("proposed", proposal.Id, "Unverified lesson requires review; no tool permissions granted.");
        }
        finally { _gate.Release(); }
    }
    /// <inheritdoc />
    public void Dispose() { _gate.Dispose(); GC.SuppressFinalize(this); }
}

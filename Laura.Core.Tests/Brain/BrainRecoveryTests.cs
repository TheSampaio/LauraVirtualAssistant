using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task SelectedTranscriptReceiptsRecoverAfterRestartWithoutReplayingOtherHistoryOrOtherVaults()
    {
        BrainService brain = Create(); var transcripts = new SqliteConversationMemory(Path.Combine(_root, "receipts.db"));
        string identity = FileBrainStorage.Hash(brain.Location);
        transcripts.Append(new(ConversationMessageSource.UserTyped, "My goal is an unrelated historical message.", DateTimeOffset.UtcNow));
        transcripts.AppendForLearning(new(ConversationMessageSource.UserTyped, "My goal is a recovered declaration.", DateTimeOffset.UtcNow), identity);
        transcripts.AppendForLearning(new(ConversationMessageSource.UserTyped, "My goal is in a different vault.", DateTimeOffset.UtcNow), FileBrainStorage.Hash("another-vault"));
        await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        learner.RecoverPending(new SqliteConversationMemory(Path.Combine(_root, "receipts.db"))); await learner.FlushAsync();
        Assert.Single(brain.Snapshot.Notes, note => note.Type == "goal"); Assert.Contains(brain.Snapshot.Notes, note => note.Body.Contains("recovered declaration", StringComparison.Ordinal));
        Assert.Empty(transcripts.ReadPendingLearning(identity)); Assert.Single(transcripts.ReadPendingLearning(FileBrainStorage.Hash("another-vault")));
        Assert.Equal(3, transcripts.ReadRecent(10).Count);
    }
    [Fact]
    public async Task CrashBetweenMarkdownWriteAndAcknowledgmentDoesNotDuplicateOrIncreaseConfidence()
    {
        BrainService brain = Create(); var transcripts = new SqliteConversationMemory(Path.Combine(_root, "receipt-retry.db"));
        string identity = FileBrainStorage.Hash(brain.Location); const string input = "My goal is an idempotent recovered declaration.";
        transcripts.AppendForLearning(new(ConversationMessageSource.UserTyped, input, DateTimeOffset.UtcNow), identity);
        await using var original = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        await original.LearnAsync(input); BrainNote before = brain.Snapshot.Notes.Single(note => note.Type == "goal");
        await using var resumed = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        resumed.RecoverPending(transcripts); await resumed.FlushAsync(); BrainNote after = brain.Snapshot.Notes.Single(note => note.Type == "goal");
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Confidence, after.Confidence); Assert.Empty(transcripts.ReadPendingLearning(identity));
    }
    [Fact]
    public async Task FailedReceiptsSurviveAndRecoverWithAHealthyServiceWithoutAnEndlessRetryLoop()
    {
        var unavailable = new BrainService(_settings, new AssistantStorage(_root), new BrainIndex(Path.Combine(_root, "failed-receipt-cache"), new Embedding(false)),
            _ => throw new IOException("Synthetic unavailable vault"), NullLogger<BrainService>.Instance, _localizer); _services.Add(unavailable);
        var transcripts = new SqliteConversationMemory(Path.Combine(_root, "receipt-failure.db")); string identity = FileBrainStorage.Hash(unavailable.Location);
        transcripts.AppendForLearning(new(ConversationMessageSource.UserTyped, "My goal is a retained failed declaration.", DateTimeOffset.UtcNow), identity);
        await using var failed = new BrainLearning(unavailable, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        failed.RecoverPending(transcripts); await failed.FlushAsync(); Assert.Equal(1, failed.Status.Failed); Assert.Single(transcripts.ReadPendingLearning(identity));
        await failed.DisposeAsync(); BrainService healthy = Create(); await using var recovered = new BrainLearning(healthy, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        recovered.RecoverPending(transcripts); await recovered.FlushAsync(); Assert.Empty(transcripts.ReadPendingLearning(identity)); Assert.Single(healthy.Snapshot.Notes, note => note.Type == "goal");
    }
    [Fact]
    public async Task HistoryClearRemovesSelectedReceiptsButNotExplicitFactsAndAssistantReceiptsAreRejected()
    {
        BrainService brain = Create(); var transcripts = new SqliteConversationMemory(Path.Combine(_root, "receipt-clear.db")); string identity = FileBrainStorage.Hash(brain.Location);
        Assert.Throws<ArgumentException>(() => transcripts.AppendForLearning(new(ConversationMessageSource.Assistant, "My goal is generated text.", DateTimeOffset.UtcNow), identity));
        transcripts.AppendForLearning(new(ConversationMessageSource.UserTyped, "My goal is a cleared event.", DateTimeOffset.UtcNow), identity);
        transcripts.SetFact("synthetic-fact", "Explicit retained fact"); transcripts.ClearHistory();
        await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        learner.RecoverPending(transcripts); await learner.FlushAsync(); Assert.Equal(0, learner.Status.Accepted); Assert.Single(transcripts.Search("retained")); Assert.Empty(brain.Snapshot.Notes);
    }
}

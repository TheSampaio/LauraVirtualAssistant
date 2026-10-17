using System.IO.Compression;
using System.Text.Json;
using Laura.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Tests.Diagnostics;

public sealed class RuntimeJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "laura-journal-" + Guid.NewGuid().ToString("N"));
    /// <summary>Detail filtering and separate task/operation identities survive the asynchronous journal without payload fields.</summary>
    [Fact]
    public async Task NativeSeverityAndOperationCorrelationAreRetained()
    {
        await using var journal = new RuntimeJournal(_root);
        Guid interaction = Guid.NewGuid(), task = Guid.NewGuid(), operation = Guid.NewGuid();
        LauraDiagnostics.Record("audio.chunk.arrival");
        using (LauraDiagnostics.Correlate(interaction))
        { LauraDiagnostics.Record("process.create_failed", amount: 5, outcome: "failed", taskId: task, operationId: operation); }
        var snapshot = await journal.SnapshotAsync();
        Assert.DoesNotContain(snapshot.Events, row => row.Operation == "audio.chunk.arrival");
        var failure = Assert.Single(snapshot.Events, row => row.Operation == "process.create_failed");
        Assert.Equal(LogLevel.Error, failure.Level); Assert.Equal(interaction, failure.InteractionId);
        Assert.Equal(task, failure.TaskId); Assert.Equal(operation, failure.OperationId);
        journal.MinimumLevel = LogLevel.Debug;
        LauraDiagnostics.Record("audio.chunk.arrival");
        Assert.Contains((await journal.SnapshotAsync()).Events, row => row.Operation == "audio.chunk.arrival");
    }
    [Fact]
    public async Task ExportNeverFormatsLoggerPayloadsAndContainsUsefulCorrelatedEvents()
    {
        await using var journal = new RuntimeJournal(_root);
        using var provider = new RuntimeLogProvider(journal); var logger = provider.CreateLogger("Laura.Test.Fixture");
        const string secret = "sk-proj-synthetic_secret_0123456789";
        Guid interaction = Guid.NewGuid();
        using (LauraDiagnostics.Correlate(interaction))
        {
            LauraDiagnostics.Record("fixture.retrieval", 12.5, 2, "lexical", ["private-user-note"], [.9]);
            logger.LogError(new IOException(secret), "Failure at {Path} with {Token}", @"C:\Users\Private\secret.ini", secret);
            logger.Log(LogLevel.Error, new EventId(7), "private text", null, (_, _) => throw new InvalidOperationException("Formatter must never run"));
        }
        RuntimeLogSnapshot snapshot = await journal.SnapshotAsync();
        var recall = Assert.Single(snapshot.Events, row => row.Operation == "fixture.retrieval");
        Assert.Equal(interaction, recall.InteractionId); Assert.Equal(12.5, recall.Milliseconds); Assert.NotEqual("private-user-note", Assert.Single(recall.Ids));
        using var output = new MemoryStream(); await DiagnosticExport.WriteAsync(output, snapshot, "2.0.0"); output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read); Assert.Equal(3, zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open()); string text = await reader.ReadToEndAsync();
            Assert.DoesNotContain(secret, text); Assert.DoesNotContain("private-user-note", text); Assert.DoesNotContain("C:\\Users", text); Assert.DoesNotContain("private text", text);
        }
        Assert.NotNull(zip.GetEntry("report.md")); Assert.NotNull(zip.GetEntry("summary.json")); Assert.NotNull(zip.GetEntry("events.jsonl"));
    }
    [Fact]
    public async Task RotationRetentionAndRestartPreserveOnlyABoundedTechnicalWindow()
    {
        Directory.CreateDirectory(_root);
        string expired = Path.Combine(_root, "runtime-20000101-expired.jsonl"); await File.WriteAllTextAsync(expired, "old"); File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8));
        await using (var journal = new RuntimeJournal(_root, 1024, 3))
        {
            for (int index = 0; index < 100; index++) { journal.Record("fixture.rotation", amount: index); }
            var snapshot = await journal.SnapshotAsync(); Assert.NotEmpty(snapshot.Events); Assert.False(File.Exists(expired));
            Assert.InRange(Directory.GetFiles(_root).Length, 1, 3);
            Assert.All(Directory.GetFiles(_root), path => Assert.InRange(new FileInfo(path).Length, 1, 1600));
        }
        await using var restarted = new RuntimeJournal(_root, 1024, 3); Assert.NotEmpty((await restarted.SnapshotAsync()).Events);
    }
    [Fact]
    public async Task StorageFailureDoesNotThrowIntoProducersAndSnapshotReportsUnavailable()
    {
        Directory.CreateDirectory(_root); string blocked = Path.Combine(_root, "not-a-directory"); await File.WriteAllTextAsync(blocked, "fixture");
        await using var journal = new RuntimeJournal(blocked); journal.Record("fixture.failure");
        await Assert.ThrowsAsync<IOException>(() => journal.SnapshotAsync()); Assert.True(journal.WriteFailures > 0);
    }
    [Fact]
    public async Task SaturatedProducersRemainBoundedAndReportDroppedEvents()
    {
        await using var journal = new RuntimeJournal(_root, 1024, 2);
        for (int index = 0; index < 30000; index++) { journal.Record("fixture.pressure", amount: index); }
        var snapshot = await journal.SnapshotAsync(); Assert.True(snapshot.DroppedEvents > 0); Assert.InRange(snapshot.Events.Count, 1, 20);
    }
    [Fact]
    public async Task CorruptOrTamperedLogRowsCannotInjectFreeTextIntoTheBundle()
    {
        await using var journal = new RuntimeJournal(_root); await journal.SnapshotAsync();
        string path = Path.Combine(_root, "runtime-20990101-tampered.jsonl");
        var row = new RuntimeLogEvent(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "Bearer private-secret-value", 0, 0, "C:\\Users\\Private", ["private-note"], []);
        await File.WriteAllTextAsync(path, "not json\n" + JsonSerializer.Serialize(row) + "\n");
        var snapshot = await journal.SnapshotAsync(); Assert.Equal(1, snapshot.MalformedLines);
        Assert.DoesNotContain(snapshot.Events, entry => entry.Operation.Contains("private-secret", StringComparison.Ordinal) || entry.Outcome.Contains("Private", StringComparison.Ordinal));
    }
    public void Dispose() { if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }
}

using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task AudioReadsWritesRetrievalAndSimulatedToolsSettleWithoutCorruptionOrDeadlock()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("Knowledge/Concurrent.md", "Concurrent fixture", "Original concurrent fixture."));
        var buffer = new PcmPlaybackBuffer(); byte[] source = new byte[48000 * 5];
        for (int index = 0; index < source.Length; index++) { source[index] = (byte)(index % 251); }
        using var capture = new PcmAudioDiagnosticCapture(); capture.Receive(source); buffer.BeginTurn();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var dispatcher = new CallbackSkillDispatcher((_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(SkillResponse.Silent()); });
        Task writer = Task.Run(async () => { for (int index = 0; index < 20; index++) { note = await brain.SaveAsync(note with { Body = "Concurrent fixture revision " + index }, timeout.Token); } });
        Task reader = Task.Run(async () => { for (int index = 0; index < 20; index++) { await brain.SearchAsync("concurrent fixture", cancellationToken: timeout.Token); } });
        Task producer = Task.Run(() => { for (int index = 0; index < source.Length; index += 961) { buffer.Add(source.AsSpan(index, Math.Min(961, source.Length - index)), Environment.TickCount64); } buffer.CompleteTurn(); });
        Task consumer = Task.Run(async () =>
        {
            byte[] output = new byte[2880];
            while (buffer.Metrics.ReadBytes < source.Length)
            { int read = buffer.Read(output, Environment.TickCount64); if (read > 0) { capture.Play(output[..read]); } else { await Task.Delay(1, timeout.Token); } }
        });
        Task tool = Task.Run(async () => { for (int index = 0; index < 20; index++) { await dispatcher.DispatchAsync(SkillRequest.Create("Synthetic tool", _localizer.Culture, DateTimeOffset.UtcNow, SkillRequestSource.Text), timeout.Token); } });
        await Task.WhenAll(writer, reader, producer, consumer, tool).WaitAsync(timeout.Token);
        Assert.True(capture.Compare().Identical); Assert.Equal(0, buffer.Metrics.MisalignedReads);
        Assert.Equal(note.Hash, FileBrainStorage.Hash(await File.ReadAllTextAsync(Path.Combine(brain.Location, note.Path))));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => brain.SaveAsync(note with { Body = "Must not persist" }, canceled.Token));
        Assert.Equal(note.Hash, brain.Snapshot.Notes.Single(item => item.Id == note.Id).Hash);
    }
    [Fact]
    public async Task SlowInitializationCannotHoldTheRealTimeWorkingContextLock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var brain = new BrainService(_settings, new(_root), new BrainIndex(Path.Combine(_root, "initialization"), new Embedding(false)),
            path => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); return new FileBrainStorage(path); }, NullLogger<BrainService>.Instance, _localizer);
        _services.Add(brain); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "initialization.db")));
        Task initialization = Task.Run(memory.Initialize);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => memory.SetWorkingContext("Current actual request")).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(Guid.Empty, memory.WorkingTurnId); Assert.Equal("Current actual request", memory.WorkingUserText);
        }
        finally { release.TrySetResult(); await initialization; }
    }
}

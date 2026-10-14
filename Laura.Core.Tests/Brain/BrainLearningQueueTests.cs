using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Localization;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

/// <summary>Exercises bounded acceptance, failure isolation and shutdown with synthetic declarations.</summary>
public sealed class BrainLearningQueueTests
{
    [Fact]
    public async Task SaturationIsReportedAndWaitingObservationResumesWithoutDroppingAcceptedWork()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-learning-" + Guid.NewGuid().ToString("N"));
        var settings = new StubSettingsService(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(root, "Brain") } });
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var brain = new BrainService(settings, new AssistantStorage(root), new BrainIndex(Path.Combine(root, "Cache"), new NoEmbedding()),
            location => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return new FileBrainStorage(location); }, NullLogger<BrainService>.Instance);
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        await using var learner = new BrainLearning(brain, settings, localizer, NullLogger<BrainLearning>.Instance);
        try
        {
            Assert.Equal(BrainObservationResult.Accepted, learner.Observe("My goal is a synthetic queue test."));
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            for (int index = 0; index < 64; index++) { Assert.Equal(BrainObservationResult.Accepted, learner.Observe("My goal is a synthetic queue test.")); }
            Assert.Equal(BrainObservationResult.Saturated, learner.Observe("My goal is a rejected queue event."));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => learner.ObserveAsync("My goal is cancelled before acceptance.", cancelled.Token));
            Task<BrainObservationResult> waiting = learner.ObserveAsync("My goal is a synthetic queue test.");
            Assert.False(waiting.IsCompleted); release.Set();
            Assert.Equal(BrainObservationResult.Accepted, await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
            await learner.FlushAsync(); Assert.Equal(66, learner.Status.Accepted); Assert.Equal(66, learner.Status.Completed);
            Assert.Equal(0, learner.Status.Pending); Assert.Equal(1, learner.Status.Saturated);
            Assert.Single(brain.Snapshot.Notes, note => note.Type == "goal");
            await learner.DisposeAsync(); Assert.Equal(BrainObservationResult.Stopped, learner.Observe("My goal is after shutdown."));
        }
        finally { release.Set(); await learner.DisposeAsync(); await brain.DisposeAsync(); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task ShutdownDrainsAcceptedWorkAndRetryableFailureRecoversWithoutDuplicatingNotes()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-learning-" + Guid.NewGuid().ToString("N")); int attempts = 0;
        var settings = new StubSettingsService(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(root, "Brain") } });
        var brain = new BrainService(settings, new AssistantStorage(root), new BrainIndex(Path.Combine(root, "Cache"), new NoEmbedding()),
            location => Interlocked.Increment(ref attempts) == 1 ? throw new IOException("Synthetic transient storage failure") : new FileBrainStorage(location), NullLogger<BrainService>.Instance);
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        var learner = new BrainLearning(brain, settings, localizer, NullLogger<BrainLearning>.Instance);
        try
        {
            Assert.Equal(BrainObservationResult.Excluded, learner.Observe("My password is synthetic."));
            Assert.Equal(BrainObservationResult.Accepted, learner.Observe("My goal is a synthetic recovered declaration."));
            await learner.DisposeAsync(); Assert.Equal(1, learner.Status.Completed); Assert.Equal(0, learner.Status.Failed); Assert.Equal(2, attempts);
            Assert.Single(brain.Snapshot.Notes, note => note.Type == "goal");
        }
        finally { await learner.DisposeAsync(); await brain.DisposeAsync(); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task PermanentFailureIsObservableAndDoesNotKillTheWorker()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-learning-" + Guid.NewGuid().ToString("N")); int attempts = 0;
        var settings = new StubSettingsService(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(root, "Brain") } });
        var brain = new BrainService(settings, new AssistantStorage(root), new BrainIndex(Path.Combine(root, "Cache"), new NoEmbedding()),
            location => Interlocked.Increment(ref attempts) == 1 ? throw new UnauthorizedAccessException("Synthetic denied vault") : new FileBrainStorage(location), NullLogger<BrainService>.Instance);
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        await using var learner = new BrainLearning(brain, settings, localizer, NullLogger<BrainLearning>.Instance);
        try
        {
            learner.Observe("My goal is a synthetic rejected declaration."); learner.Observe("My goal is a synthetic valid declaration.");
            await learner.FlushAsync(); Assert.Equal(1, learner.Status.Failed); Assert.Equal(1, learner.Status.Completed); Assert.Equal(0, learner.Status.Pending);
            Assert.Single(brain.Snapshot.Notes, note => note.Type == "goal");
        }
        finally { await learner.DisposeAsync(); await brain.DisposeAsync(); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    private sealed class NoEmbedding : IBrainEmbedding
    {
        public string ModelId => "";
        public float[] Embed(string text) => [];
    }
}

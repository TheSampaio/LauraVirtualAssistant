using System.Diagnostics;
using System.Text.Json;
using Laura.App.Views;
using Laura.Core.Brain;
using Laura.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Reproducible native UI/write benchmark using only an isolated synthetic vault, without cloud or microphone access.</summary>
internal static class BrainPerformanceSmokeRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        var brain = services.GetRequiredService<BrainService>();
        for (int index = 0; index < 80; index++)
        {
            await brain.SaveAsync(new BrainNote { Path = $"Knowledge/Fixture{index}.md", Title = $"Synthetic fixture {index}", Body = "# Synthetic fixture\n\nBenchmark knowledge only.", Type = "fact" }).ConfigureAwait(true);
        }
        var navigation = (NavigationPage)window.Page!;
        var memory = services.GetRequiredService<MemoryPage>();
        await navigation.PushAsync(memory, false).ConfigureAwait(true);
        await Task.Delay(500).ConfigureAwait(true);
        using var capture = LauraDiagnostics.Capture();
        using var correlation = LauraDiagnostics.Correlate(Guid.NewGuid());
        var frames = new List<double>(); long previous = Stopwatch.GetTimestamp();
        var timer = memory.Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) => { long now = Stopwatch.GetTimestamp(); frames.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; };
        using var process = Process.GetCurrentProcess(); TimeSpan cpuStart = process.TotalProcessorTime;
        long allocations = GC.GetTotalAllocatedBytes(); long start = Stopwatch.GetTimestamp();
        timer.Start();
        try
        {
            using var audioTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            Task<Laura.Core.Conversation.PcmPlaybackMetrics>? audio = Environment.GetCommandLineArgs().Contains("--brain-audio-concurrency-smoke", StringComparer.Ordinal)
                ? Laura.Platform.Windows.Conversation.GeminiLiveClient.VerifyFragmentedPlaybackForSmokeAsync(audioTimeout.Token, 90) : null;
            BrainNote note = brain.Snapshot.Notes.Single(item => item.Path == "Knowledge/Fixture0.md");
            for (int index = 0; index < 32; index++)
            {
                note = await brain.SaveAsync(note with { Body = $"# Synthetic fixture\n\nRevision {index} retains the same graph topology." }).ConfigureAwait(true);
            }
            await Task.Delay(750).ConfigureAwait(true);
            if (audio is not null)
            {
                var audioMetrics = await audio.ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, "concurrent-audio.json"), JsonSerializer.Serialize(audioMetrics, JsonOptions)).ConfigureAwait(true);
                if (audioMetrics.MisalignedReads != 0 || audioMetrics.ReadBytes != audioMetrics.AddedBytes)
                { throw new InvalidOperationException("Concurrent native audio failed sample alignment or byte preservation."); }
            }
            process.Refresh();
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double[] ordered = frames.Order().ToArray();
            var samples = LauraDiagnostics.Snapshot();
            var metrics = new
            {
                Fixture = "80 initial facts, 32 revisions, native visible memory page, no network/microphone", Writes = 32,
                ElapsedMs = elapsed, UiTicks = frames.Count,
                UiP95Ms = ordered.Length == 0 ? 0 : ordered[(int)((ordered.Length - 1) * .95)], UiMaxMs = ordered.LastOrDefault(),
                CpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds, process.WorkingSet64,
                AllocatedBytes = GC.GetTotalAllocatedBytes() - allocations,
                GcPauseMs = GC.GetGCMemoryInfo().PauseDurations.ToArray().Sum(duration => duration.TotalMilliseconds),
                Operations = samples.GroupBy(sample => sample.Operation).Select(group => new { Name = group.Key, Count = group.Count(), TotalMs = group.Sum(sample => sample.Milliseconds), MaxMs = group.Max(sample => sample.Milliseconds) }).ToArray(),
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "brain-performance.json"), JsonSerializer.Serialize(metrics, JsonOptions)).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "brain-diagnostics.json"), JsonSerializer.Serialize(samples, JsonOptions)).ConfigureAwait(true);
            if (frames.Count == 0 || brain.Snapshot.Notes.Single(item => item.Id == note.Id).Hash != note.Hash)
            { throw new InvalidOperationException("UI heartbeat or persisted final revision was not verified."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Native UI heartbeat and concurrent synthetic Brain writes measured; actual metrics are in brain-performance.json.").ConfigureAwait(true);
        }
        finally { timer.Stop(); }
    }
}

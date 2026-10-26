using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Core.Brain;

namespace Laura.App.Infrastructure;

/// <summary>Renders large and local graph fixtures in native MAUI without accessing the owner's vault or microphone.</summary>
internal static class BrainGraphSmokeRunner
{
    /// <summary>Captures overview, zoom and local topology using only synthetic nodes and authored fixture links.</summary>
    internal static async Task RunAsync(Window window, string directory)
    {
        var navigation = (NavigationPage)window.Page!;
        try
        {
            var graph = new BrainGraphView();
            var page = new ContentPage { BackgroundColor = Theme.Background, Content = graph, Padding = 24 };
            await navigation.PushAsync(page, false).ConfigureAwait(true);
            await Task.Delay(250).ConfigureAwait(true);
            BrainNote[] notes = Enumerable.Range(0, 1062).Select(index => new BrainNote
            {
                Id = $"fixture-{index}", Path = $"Knowledge/Fixture{index}.md", Title = $"Synthetic node {index}",
                OriginAgent = index % 7 == 0 ? "codex" : "laura",
            }).ToArray();
            BrainEdge[] edges = Enumerable.Range(6, 996).Select(index => new BrainEdge(notes[index % 6].Id, notes[index].Id, "uses", 1, 1, "Synthetic explicit link")).ToArray();
            var snapshot = new BrainSnapshot(notes, edges, []);
            await graph.SetGraphAsync(snapshot, "", "", "", null, 1200).ConfigureAwait(true);
            if (graph.VisibleNodeCount != notes.Length) { throw new InvalidOperationException("Large graph lost visible fixture nodes."); }
            await CaptureAsync(window, directory, "graph-large").ConfigureAwait(true);
            graph.Zoom(1.8f);
            await CaptureAsync(window, directory, "graph-zoom").ConfigureAwait(true);
            await graph.SetGraphAsync(snapshot, "", "", "", notes[0].Id, 1200).ConfigureAwait(true);
            if (graph.VisibleNodeCount != 167) { throw new InvalidOperationException("Local graph did not preserve its authored neighborhood."); }
            await CaptureAsync(window, directory, "graph-local").ConfigureAwait(true);
            await graph.VerifyInteractionsForSmokeAsync(notes[0].Id, notes[6].Id).ConfigureAwait(true);
            await CaptureAsync(window, directory, "graph-after-drag").ConfigureAwait(true);
            graph.SetCommunities(true);
            await graph.SetGraphAsync(snapshot, "", "", "", null, 1200).ConfigureAwait(true);
            await CaptureAsync(window, directory, "graph-communities").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: native graph rendered 1062 synthetic nodes, straight undirected lines, zoom, 167-node local neighborhood and component grouping; pointer selection, neighbor motion during drag, background deselection and animation pause/resume passed. No personal vault or microphone accessed.").ConfigureAwait(true);
            if (Environment.GetCommandLineArgs().Contains("--brain-graph-interactive", StringComparer.Ordinal))
            {
                await graph.SetGraphAsync(snapshot, "", "", "", notes[0].Id, 1200).ConfigureAwait(true);
                graph.ResetView();
                await File.WriteAllTextAsync(Path.Combine(directory, "interaction-ready.txt"), "Synthetic native graph ready for pointer interaction.").ConfigureAwait(true);
                DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                while (!File.Exists(Path.Combine(directory, "interaction-done.txt")) && DateTime.UtcNow < deadline)
                { await Task.Delay(200).ConfigureAwait(true); }
            }
        }
        finally { Application.Current!.Quit(); }
    }

    /// <summary>Waits for native drawing before capturing Laura's own window content.</summary>
    private static async Task CaptureAsync(Window window, string directory, string name)
    {
        await Task.Delay(250).ConfigureAwait(true);
        await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, name + ".png")).ConfigureAwait(true);
    }
}

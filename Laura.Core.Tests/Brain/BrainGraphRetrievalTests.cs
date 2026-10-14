using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task ActualRetrievalFollowsTwoAuthoredHopsAndRespectsPersistedDepthWithoutWritingNewRelations()
    {
        BrainService brain = Create(); BrainNote allocator = await brain.SaveAsync(Note("Knowledge/Allocator.md", "Allocator", "A fictional memory arena."));
        BrainNote runtime = await brain.SaveAsync(Note("Knowledge/Runtime.md", "Runtime", "A fictional execution layer.") with
            { Relations = [new(allocator.Id, "depends_on", 1, 1, "Synthetic manifest evidence", "manifest")] });
        BrainNote engine = await brain.SaveAsync(Note("Projects/Engine.md", "SeedChronometer", "SeedChronometer is a fictional project.") with
            { Relations = [new(runtime.Id, "uses", 1, 1, "Synthetic user declaration", "user_declaration")] });
        string[] originalHashes = brain.Snapshot.Notes.Select(note => note.Hash).ToArray(); int edgeCount = brain.Snapshot.Edges.Count;
        BrainHit reached = Assert.Single(await brain.SearchAsync("SeedChronometer"), hit => hit.Note.Id == allocator.Id);
        Assert.Equal(0, reached.Lexical); Assert.Equal(0, reached.Semantic); Assert.True(reached.Graph > 0); Assert.NotNull(reached.Activation);
        Assert.Equal(2, reached.Activation.Path.Count); Assert.Equal("manifest", reached.Activation.Path[1].Edge.Provenance);
        Assert.Equal(edgeCount, brain.Snapshot.Edges.Count); Assert.Equal(originalHashes, brain.Snapshot.Notes.Select(note => note.Hash));
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { RetrievalGraphDepth = 1 } });
        Assert.DoesNotContain(await brain.SearchAsync("SeedChronometer"), hit => hit.Note.Id == allocator.Id);
        Assert.Contains(await brain.SearchAsync("SeedChronometer"), hit => hit.Note.Id == runtime.Id);
        Assert.Contains(brain.Snapshot.Edges, edge => edge.Source == engine.Id && edge.Target == runtime.Id && edge.Provenance == "user_declaration");
    }
}

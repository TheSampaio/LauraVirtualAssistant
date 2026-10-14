using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

/// <summary>Checks bounded authored associations, direction, attribution and cancellation without fabricated semantic edges.</summary>
public sealed class BrainGraphActivationTests
{
    private static BrainNote Node(string id) => new() { Id = id, Path = "Knowledge/" + id + ".md", Title = id, Body = "Synthetic unrelated contents." };
    [Fact]
    public void TwoHopTraversalPreservesActualEvidenceAndAttenuatesWithoutCrossingContradictions()
    {
        var nodes = new[] { Node("engine"), Node("runtime"), Node("allocator"), Node("contradicted"), Node("isolated") };
        var uses = new BrainEdge("engine", "runtime", "uses", 1, 1, "Synthetic explicit dependency", "user_declaration");
        var depends = new BrainEdge("runtime", "allocator", "depends_on", 1, 1, "Synthetic verified manifest", "manifest");
        var forbidden = new BrainEdge("runtime", "contradicted", "contradicts", 1, 1, "Contrary evidence", "manual");
        var graph = new BrainGraphActivation(new(nodes, [uses, depends, forbidden], []));
        var hits = graph.Expand(new Dictionary<string, double> { ["engine"] = 1 }, new() { RetrievalGraphDepth = 2 });
        Assert.Equal(2, hits.Count); Assert.Equal(1, hits["runtime"].Score); Assert.Equal(.55, hits["allocator"].Score, 6);
        Assert.Equal(new[] { uses, depends }, hits["allocator"].Path.Select(step => step.Edge)); Assert.All(hits["allocator"].Path, step => Assert.False(step.Reversed));
        Assert.DoesNotContain("contradicted", hits.Keys); Assert.DoesNotContain("isolated", hits.Keys);
        Assert.DoesNotContain("allocator", graph.Expand(new Dictionary<string, double> { ["engine"] = 1 }, new() { RetrievalGraphDepth = 1 }).Keys);
    }
    [Fact]
    public void ReverseTraversalIsLabelledAndWeakerWhileCyclesRemainBounded()
    {
        var graph = new BrainGraphActivation(new([Node("one"), Node("two"), Node("three")],
            [new("one", "two", "uses", 1, 1, "Actual edge"), new("two", "three", "uses", 1, 1, "Actual edge"), new("three", "one", "uses", 1, 1, "Actual edge")], []));
        var hits = graph.Expand(new Dictionary<string, double> { ["two"] = 1 }, new() { RetrievalGraphDepth = 4 });
        Assert.Equal(2, hits.Count); Assert.Equal(.7, hits["one"].Score, 6); Assert.True(Assert.Single(hits["one"].Path).Reversed);
        Assert.Empty(graph.Expand(new Dictionary<string, double> { ["two"] = 1 }, new() { RetrievalGraphDepth = 0 }));
    }
    [Fact]
    public void DenseGraphHonorsCandidateBudgetAndExcludesUntrustedStructureAndNonKnowledgeNodes()
    {
        var nodes = Enumerable.Range(0, 80).Select(index => Node("node" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var edges = nodes.Skip(1).Select(node => new BrainEdge(nodes[0].Id, node.Id, "uses", 1, 1, "Synthetic explicit relation")).ToArray();
        var graph = new BrainGraphActivation(new(nodes, edges, []));
        Assert.InRange(graph.Expand(new Dictionary<string, double> { [nodes[0].Id] = 1 }, new() { RetrievalGraphCandidates = 8 }).Count, 1, 8);
        var draft = Node("draft") with { Status = "draft" }; var skill = Node("skill") with { Type = "skill" }; var conflict = Node("conflict");
        var isolated = new BrainGraphActivation(new([nodes[0], draft, skill, conflict, Node("unknown")],
            [new(nodes[0].Id, "draft", "uses", 1, 1, "Draft"), new(nodes[0].Id, "skill", "uses", 1, 1, "Skill"), new(nodes[0].Id, "conflict", "uses", 1, 1, "Conflicted"),
                new(nodes[0].Id, "unknown", "execute_anything", 1, 1, "Unsupported relation")], [new(conflict.Path, "conflicting_id")]));
        Assert.Empty(isolated.Expand(new Dictionary<string, double> { [nodes[0].Id] = 1 }, new()));
    }
    [Fact]
    public void CancellationAndInvalidWeightsCannotStartAnUnboundedTraversal()
    {
        var graph = new BrainGraphActivation(new([Node("one"), Node("two")], [new("one", "two", "uses", double.PositiveInfinity, 1, "Invalid strength")], []));
        Assert.Empty(graph.Expand(new Dictionary<string, double> { ["one"] = 1 }, new()));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => graph.Expand(new Dictionary<string, double> { ["one"] = 1 }, new(), cancellation.Token));
        BrainOptions bounded = new BrainOptions { RetrievalGraphDepth = 1000, RetrievalGraphCandidates = int.MaxValue, RetrievalGraphMilliseconds = -1 }.Sanitized();
        Assert.Equal(4, bounded.RetrievalGraphDepth); Assert.Equal(500, bounded.RetrievalGraphCandidates); Assert.Equal(5, bounded.RetrievalGraphMilliseconds);
    }
}

using System.Numerics;
using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

/// <summary>Protects graph spacing and connected structure against perimeter collapse.</summary>
public sealed class BrainGraphLayoutTests
{
    /// <summary>Creates reproducible synthetic note identities without accessing the personal vault.</summary>
    private static BrainNote[] Nodes(int count) => Enumerable.Range(0, count).Select(index => new BrainNote { Id = $"node-{index}", Title = $"Node {index}" }).ToArray();

    /// <summary>Checks that an empty or singleton graph remains finite and deterministic.</summary>
    [Fact]
    public void EmptyAndSingletonGraphsRemainValid()
    {
        Assert.Empty(BrainGraphLayout.Calculate([], []));
        BrainNote[] notes = Nodes(1);
        var first = BrainGraphLayout.Calculate(notes, []);
        Assert.Equal(first[notes[0].Id], BrainGraphLayout.Calculate(notes, [])[notes[0].Id]);
        Assert.True(float.IsFinite(first[notes[0].Id].X));
        Assert.True(float.IsFinite(first[notes[0].Id].Y));
    }

    /// <summary>Checks that a vault-sized graph occupies its interior and retains space between neighbors.</summary>
    [Fact]
    public void ThousandDisconnectedNodesFillAnAreaWithSeparatedNeighbors()
    {
        BrainNote[] notes = Nodes(1062);
        Vector2[] points = BrainGraphLayout.Calculate(notes, []).Values.ToArray();
        Vector2 center = points.Aggregate(Vector2.Zero, (sum, point) => sum + point) / points.Length;
        float radius = points.Max(point => Vector2.Distance(point, center));
        Assert.True(points.Count(point => Vector2.Distance(point, center) < radius * .6f) > points.Length / 4);
        for (int left = 0; left < points.Length; left++)
        {
            for (int right = left + 1; right < points.Length; right++)
            {
                Assert.True(Vector2.Distance(points[left], points[right]) > 15);
            }
        }
    }

    /// <summary>Checks that a real hub moves into the center rather than remaining on an initialization ring.</summary>
    [Fact]
    public void ConnectedHubMovesInsideItsNeighborsFromLegacyRingPositions()
    {
        BrainNote[] notes = Nodes(61);
        BrainEdge[] edges = notes.Skip(1).Select(note => new BrainEdge(notes[0].Id, note.Id, "uses", 1, 1, "Synthetic link")).ToArray();
        var ring = notes.Select((note, index) => (note.Id, Point: new Vector2(MathF.Cos(index * 2 * MathF.PI / notes.Length), MathF.Sin(index * 2 * MathF.PI / notes.Length)) * 300))
            .ToDictionary(pair => pair.Id, pair => pair.Point);
        var positions = BrainGraphLayout.Calculate(notes, edges, ring);
        Vector2 hub = positions[notes[0].Id];
        Vector2 center = notes.Skip(1).Aggregate(Vector2.Zero, (sum, note) => sum + positions[note.Id]) / (notes.Length - 1);
        Assert.True(Vector2.Distance(hub, center) < 25);
        Assert.All(notes.Skip(1), note => Assert.InRange(Vector2.Distance(hub, positions[note.Id]), 40, 400));
    }

    /// <summary>Checks that independent connected groups stay closer internally than to another group.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredComponentsClusterWithoutInventingEdges(bool grouped)
    {
        BrainNote[] notes = Nodes(42);
        BrainEdge[] edges = Enumerable.Range(1, 20).SelectMany(index => new[]
        {
            new BrainEdge(notes[0].Id, notes[index].Id, "uses", 1, 1, "First component"),
            new BrainEdge(notes[21].Id, notes[index + 21].Id, "uses", 1, 1, "Second component"),
        }).ToArray();
        var positions = BrainGraphLayout.Calculate(notes, edges, communities: grouped ? BrainGraphTopology.Communities(notes, edges) : null);
        float between = Vector2.Distance(positions[notes[0].Id], positions[notes[21].Id]);
        float within = notes.Skip(1).Take(20).Average(note => Vector2.Distance(positions[notes[0].Id], positions[note.Id]));
        Assert.True(between > within * 2);
        Assert.Equal(notes.Length, positions.Count);
    }

    /// <summary>Checks coincident cached coordinates and invalid links cannot leave overlapping or nonfinite nodes.</summary>
    [Fact]
    public void CoincidentCacheAndInvalidEdgesAreHandled()
    {
        BrainNote[] notes = Nodes(12);
        var cache = notes.ToDictionary(note => note.Id, _ => Vector2.Zero);
        BrainEdge[] edges = [new(notes[0].Id, "missing", "uses", 1, 1, "Missing target"), new(notes[0].Id, notes[1].Id, "uses", double.NaN, 1, "Invalid weight")];
        var positions = BrainGraphLayout.Calculate(notes, edges, cache);
        Assert.All(positions.Values, point => Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y)));
        Assert.Equal(notes.Length, positions.Values.Distinct().Count());
    }
}

using System.Numerics;
using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

/// <summary>Verifies responsive, frame-independent graph motion using synthetic geometry.</summary>
public sealed class BrainGraphMotionTests
{
    /// <summary>Creates a hub, two neighbors and a disconnected node at known world coordinates.</summary>
    private static BrainGraphMotion Fixture() => new(new Dictionary<string, Vector2>
    {
        ["hub"] = Vector2.Zero, ["left"] = new(-100, 0), ["right"] = new(100, 0), ["orphan"] = new(0, 400),
    }, [new("hub", "left", "uses", 1, 1, "Fixture"), new("hub", "right", "uses", 1, 1, "Fixture")]);

    /// <summary>Advances deterministic visible time without sleeping.</summary>
    private static void Advance(BrainGraphMotion motion, int frames, float seconds = 1f / 60)
    { for (int frame = 0; frame < frames; frame++) { motion.Advance(seconds); } }

    /// <summary>Checks that gentle idle motion continues without changing the graph's scale.</summary>
    [Fact]
    public void IdleNodesFloatWithinABoundedNeighborhood()
    {
        var motion = Fixture(); Vector2 before = motion.Position("orphan");
        Advance(motion, 120);
        Assert.InRange(Vector2.Distance(before, motion.Position("orphan")), .05f, 8);
        Advance(motion, 3600);
        Assert.InRange(Vector2.Distance(before, motion.Position("orphan")), .05f, 12);
    }

    /// <summary>Checks a pinned hub pulls its authored neighbors while disconnected nodes remain independent.</summary>
    [Fact]
    public void DraggedHubPullsConnectedNodesAndRemainsPinned()
    {
        var motion = Fixture(); Vector2 pointer = new(0, 120);
        motion.Hold("hub", pointer); Advance(motion, 120);
        Assert.Equal(pointer, motion.Position("hub"));
        Assert.True(motion.Position("left").Y > 35);
        Assert.True(motion.Position("right").Y > 35);
        Assert.InRange(Vector2.Distance(new(0, 400), motion.Position("orphan")), 0, 8);
        Vector2 neighbor = motion.Position("left");
        motion.Release(); motion.Advance(1f / 60);
        Assert.InRange(Vector2.Distance(neighbor, motion.Position("left")), 0, 9);
        Advance(motion, 600);
        Assert.All(new[] { "hub", "left", "right" }, id => Assert.True(float.IsFinite(motion.Position(id).X) && float.IsFinite(motion.Position(id).Y)));
    }

    /// <summary>Checks matching elapsed time produces matching trajectories at different render rates.</summary>
    [Fact]
    public void IntegrationDoesNotDependOnRenderFrameRate()
    {
        var smooth = Fixture(); var coarse = Fixture();
        smooth.Hold("hub", new(80, 100)); coarse.Hold("hub", new(80, 100));
        Advance(smooth, 120); Advance(coarse, 60, 1f / 30);
        Assert.True(Vector2.Distance(smooth.Position("left"), coarse.Position("left")) < .001f);
    }

    /// <summary>Checks coincident nodes separate and invalid elapsed time cannot corrupt geometry.</summary>
    [Fact]
    public void CoincidentNodesSeparateAndInvalidTimeIsIgnored()
    {
        var motion = new BrainGraphMotion(new Dictionary<string, Vector2> { ["a"] = Vector2.Zero, ["b"] = Vector2.Zero }, []);
        motion.Advance(float.NaN); motion.Advance(float.PositiveInfinity); motion.Advance(-1);
        Assert.Equal(Vector2.Zero, motion.Position("a"));
        Advance(motion, 180);
        Assert.True(Vector2.Distance(motion.Position("a"), motion.Position("b")) > 20);
    }
}

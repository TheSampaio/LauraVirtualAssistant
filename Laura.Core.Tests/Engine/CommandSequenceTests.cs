using Laura.Core.Engine;

namespace Laura.Core.Tests.Engine;

public sealed class CommandSequenceTests
{
    [Fact]
    public void SupportsExplicitStepsAndDoesNotSplitOrdinaryNotes()
    {
        Assert.Equal(["what time is it", "list assistant files"], CommandSequence.Parse("run steps: what time is it; list assistant files", ["run steps:"]));
        Assert.Null(CommandSequence.Parse("save note First; second", ["run steps:"]));
    }
    [Theory]
    [InlineData("run steps: a;b;c;d;e")]
    [InlineData("run steps: a;;b")]
    [InlineData("run steps: run steps: a")]
    public void RejectsOverflowEmptyAndRecursiveSequences(string text)
    {
        Assert.Empty(CommandSequence.Parse(text, ["run steps:"])!);
    }
}

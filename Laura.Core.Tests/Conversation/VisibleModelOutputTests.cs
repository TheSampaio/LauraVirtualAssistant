using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks that token boundaries cannot expose private local-model reasoning in chat or streaming events.</summary>
public sealed class VisibleModelOutputTests
{
    [Fact]
    public void SplitThinkingMarkersAndIncompleteReasoningNeverReachVisibleOutput()
    {
        var filter = new VisibleModelOutput();
        Assert.Equal("", filter.Append(" \n<th")); Assert.Equal("", filter.Append("ink>private details</thi"));
        Assert.Equal("Visible", filter.Append("nk>\nVisible")); Assert.Equal(" answer", filter.Append(" answer")); Assert.False(filter.Incomplete);
        var unfinished = new VisibleModelOutput(true); Assert.Equal("", unfinished.Append("private incomplete reasoning")); Assert.True(unfinished.Incomplete);
    }
    [Fact]
    public void OrdinaryAnswersArePreservedAcrossInitialPartialChunks()
    {
        var filter = new VisibleModelOutput(); Assert.Equal("", filter.Append("")); Assert.Equal("Hello", filter.Append("Hello")); Assert.Equal(" world", filter.Append(" world"));
        Assert.False(filter.Incomplete);
    }
}

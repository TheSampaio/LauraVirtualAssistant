using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Theory]
    [InlineData("Olá!", false)]
    [InlineData("Thanks.", false)]
    [InlineData("Abre o Neural Screen", true)]
    [InlineData("editor", true)]
    [InlineData("What do you know about me?", true)]
    public void RetrievalSkipsOnlyKnownTrivialMessages(string input, bool expected) => Assert.Equal(expected, BrainContextBuilder.NeedsKnowledge(input));

    [Fact]
    public async Task TrivialMessageDoesNotInitializeBrainAndCacheInvalidatesAfterCorrection()
    {
        BrainService brain = Create(); var builder = new BrainContextBuilder(brain, _settings);
        Assert.Equal("", (await builder.BuildAsync("hello")).Memories); Assert.Empty(brain.Snapshot.Notes);
        BrainNote note = await brain.SaveAsync(Note("Knowledge/Fixture.md", "Synthetic editor preference", "Preferred editor is Old Editor."));
        string first = (await builder.BuildAsync("editor")).Memories;
        Assert.Contains("Old Editor", first); Assert.Equal(first, (await builder.BuildAsync("editor")).Memories);
        await brain.SaveAsync(note with { Body = "Preferred editor is New Editor." });
        string corrected = (await builder.BuildAsync("editor")).Memories;
        Assert.Contains("New Editor", corrected); Assert.DoesNotContain("Old Editor", corrected);
        await _settings.UpdateAsync(_settings.Current with { RememberConversations = false });
        Assert.Equal("", (await builder.BuildAsync("editor")).Memories);
    }

    [Fact]
    public async Task CleanRefreshReusesSnapshotAndCanceledContextDoesNotRunRetrieval()
    {
        BrainService brain = Create(); await brain.InitializeAsync(); await brain.RefreshAsync();
        BrainSnapshot snapshot = brain.Snapshot; await brain.RefreshIfDirtyAsync(); Assert.Same(snapshot, brain.Snapshot);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BrainContextBuilder(brain, _settings).BuildAsync("editor", cancellation.Token));
    }
}

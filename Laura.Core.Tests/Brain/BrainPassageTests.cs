using Laura.Core.Brain;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task LongDocumentRetrievesLatePassageAndExpandsOnlyTheActualCurrentVersion()
    {
        BrainService brain = Create();
        string body = "# Synthetic Manual\n\n" + string.Concat(Enumerable.Repeat("Ordinary introductory material about fictional gardens.\n\n", 80))
            + "## Network Recovery\n\nThe chronometer resolves retry exhaustion with jitter-window consensus.\n\nFurther fictional details.";
        BrainNote note = await brain.SaveAsync(Note("Knowledge/Manual.md", "Synthetic Manual", body));
        BrainHit hit = Assert.Single(await brain.SearchAsync("How does chronometer resolve retry exhaustion?"));
        Assert.Contains("jitter-window consensus", hit.Excerpt, StringComparison.Ordinal); Assert.True(hit.ExcerptStart > 3000); Assert.NotNull(hit.Passage);
        Assert.Contains("Network Recovery", hit.Passage.Heading, StringComparison.Ordinal);
        Assert.Contains("jitter-window consensus", await brain.ReadPassageAsync(note.Id, note.Hash, hit.ExcerptStart, 1000), StringComparison.Ordinal);
        BrainNote changed = await brain.SaveAsync(note with { Body = body + "\nChanged source." });
        await Assert.ThrowsAsync<IOException>(() => brain.ReadPassageAsync(note.Id, note.Hash, hit.ExcerptStart));
        await Assert.ThrowsAsync<ArgumentException>(() => brain.ReadPassageAsync(changed.Id, changed.Hash, -1));
        await Assert.ThrowsAsync<ArgumentException>(() => brain.ReadPassageAsync(changed.Id, changed.Hash, 0, 4001));
    }
    [Fact]
    public async Task PassageVectorsCoverLateKnowledgeBeyondTheProvidersInputWindowAndReuseCachedUnchangedSpans()
    {
        var embedding = new PrefixEmbedding(); BrainService brain = Create(embedding);
        BrainNote note = await brain.SaveAsync(Note("Knowledge/Guide.md", "Garden guide", string.Concat(Enumerable.Repeat("Orchids grow in this fictional garden.\n\n", 100)) + "\n## Transport\n\nAn automobile carries passengers."));
        BrainHit hit = Assert.Single(await brain.SearchAsync("car"));
        Assert.Equal(note.Id, hit.Note.Id); Assert.True(hit.Semantic > .9); Assert.Equal(0, hit.Lexical); Assert.Contains("automobile", hit.Excerpt, StringComparison.Ordinal);
        int before = embedding.Calls; await brain.RefreshAsync(); Assert.Equal(before, embedding.Calls);
        await brain.DeleteAsync(note.Path); Assert.Empty(await brain.SearchAsync("car"));
    }
    [Fact]
    public void PassageBoundsCoverTailWithoutSplittingSurrogatesOrTreatingCodeAsAHeading()
    {
        string body = "# Real Heading\n\n```text\n# Fake Heading\n```\n\n" + string.Concat(Enumerable.Repeat("Synthetic 🌙 sentence.\n\n", 5000)) + "Final Sentinel";
        var note = Note("Knowledge/Bounds.md", "Bounds", body); var passages = BrainPassages.Split(note);
        Assert.InRange(passages.Count, 2, 512); Assert.Equal(body.Length, passages[^1].Start + passages[^1].Length); Assert.Equal(0, passages[0].Start);
        Assert.All(passages, passage => { Assert.DoesNotContain("Fake Heading", passage.Heading, StringComparison.Ordinal); Assert.False(char.IsLowSurrogate(body[passage.Start])); Assert.False(char.IsHighSurrogate(body[passage.Start + passage.Length - 1])); });
        for (int index = 1; index < passages.Count; index++) { Assert.True(passages[index].Start <= passages[index - 1].Start + passages[index - 1].Length); }
        Assert.Equal(body, note.Body);
    }
    [Fact]
    public async Task SkillsAndArchivedSourcesCannotBeExpandedAsKnowledgeAndDeletedDerivedIndexRebuildsFromMarkdown()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("Knowledge/Passage.md", "Synthetic passage", "Synthetic quasar lens."));
        BrainNote skill = await brain.SaveAsync(Note("Skills/Example/SKILL.md", "Example skill", "Untrusted instruction.") with { Type = "skill" });
        await Assert.ThrowsAsync<IOException>(() => brain.ReadPassageAsync(skill.Id, skill.Hash, 0));
        var index = Directory.EnumerateFiles(Path.Combine(_root, "cache"), "*.db").Single(); File.Delete(index); await brain.RefreshAsync();
        Assert.Contains(await brain.SearchAsync("quasar"), hit => hit.Note.Id == note.Id);
        await brain.DeleteAsync(note.Path); await Assert.ThrowsAsync<IOException>(() => brain.ReadPassageAsync(note.Id, note.Hash, 0));
    }
    private sealed class PrefixEmbedding : IBrainEmbedding
    {
        public int Calls { get; private set; }
        public string ModelId => "synthetic-prefix-model";
        public float[] Embed(string text)
        {
            Calls++; string prefix = text[..Math.Min(200, text.Length)];
            return prefix.Contains("automobile", StringComparison.OrdinalIgnoreCase) || prefix.Contains("car", StringComparison.OrdinalIgnoreCase) ? [1, 0] : [0, 1];
        }
    }
}

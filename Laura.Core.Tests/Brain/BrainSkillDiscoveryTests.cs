using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private BrainSkills Discovery(BrainService brain, IBrainEmbedding? embedding = null) => new(brain, _settings, discovery: new BrainIndex(Path.Combine(_root, "SkillCache"), embedding ?? new Embedding(false)));
    private static string FirstSkill(string result)
    { using var json = JsonDocument.Parse(result); return json.RootElement.GetProperty("skills")[0].GetProperty("id").GetString()!; }
    [Fact]
    public async Task TaskMetadataDiscoveryFindsSkillsOmittedByTheAlphabeticalBudgetWithoutLoadingInstructions()
    {
        BrainService brain = Create(); BrainSkills skills = Discovery(brain);
        for (int index = 0; index < 15; index++) { await skills.ImportAsync("---\nname: A garden " + index.ToString(CultureInfo.InvariantCulture) + "\ndescription: Arrange fictional garden ornaments.\n---\nPrivate instruction marker.", "Garden"); }
        var cases = new[] { "SQLite query plan", "video subtitle translation", "Markdown backlink repair", "process CPU diagnostics", "essay natural phrasing" };
        var expected = new List<BrainNote>();
        foreach (string query in cases) { expected.Add(await skills.ImportAsync("---\nname: Z " + query + "\ndescription: " + query + " procedure.\n---\nPrivate instruction marker.", "Target")); }
        string baseline = skills.Catalogue(1500); Assert.All(expected, note => Assert.DoesNotContain(note.Id, baseline, StringComparison.Ordinal));
        var timings = new List<double>();
        for (int index = 0; index < cases.Length; index++)
        {
            string result = await skills.DiscoverAsync(cases[index]); Assert.Equal(expected[index].Id, FirstSkill(result)); Assert.True(result.Length <= 1500);
            Assert.DoesNotContain("Private instruction marker", result, StringComparison.Ordinal);
            for (int repeat = 0; repeat < 5; repeat++) { var watch = Stopwatch.StartNew(); await skills.DiscoverAsync(cases[index]); timings.Add(watch.Elapsed.TotalMilliseconds); }
        }
        timings.Sort(); Console.WriteLine($"Skill metadata benchmark: 20 synthetic skills; alphabetical budget coverage 0/5; targeted lexical P@1 5/5; 25 warm queries p50={timings[12]:F2}ms, p95={timings[23]:F2}ms. No real neural model/cloud used.");
        Assert.DoesNotContain(await brain.SearchAsync(cases[0]), hit => hit.Note.Type == "skill"); Assert.Empty(_settings.Current.Brain.TrustedSkills);
    }
    [Fact]
    public async Task LocalSemanticContractFindsCrossLanguageDescriptionWithoutEmbeddingBodiesAndReusesUnchangedMetadata()
    {
        BrainService brain = Create(); var embedding = new SkillEmbedding(); BrainSkills skills = Discovery(brain, embedding);
        BrainNote writing = await skills.ImportAsync("---\nname: Synthetic text craft\ndescription: Edit essays using natural phrasing.\n---\nPrivate instruction marker.", "Writing");
        await skills.ImportAsync("---\nname: Synthetic garden\ndescription: Arrange fictional garden ornaments.\n---\nPrivate instruction marker.", "Garden");
        string result = await skills.DiscoverAsync("melhorar minha redação"); Assert.Equal(writing.Id, FirstSkill(result)); Assert.Contains("metadata_hybrid", result, StringComparison.Ordinal);
        int before = embedding.Texts.Count; await brain.SaveAsync(writing with { Body = "Changed private instruction marker." });
        result = await skills.DiscoverAsync("melhorar minha redação"); Assert.Equal(writing.Id, FirstSkill(result)); Assert.Equal(before + 1, embedding.Texts.Count);
        Assert.DoesNotContain(embedding.Texts, text => text.Contains("instruction marker", StringComparison.Ordinal));
        Assert.Contains("review_required", result, StringComparison.Ordinal); Assert.Empty(_settings.Current.Brain.TrustedSkills);
    }
    [Fact]
    public async Task DiscoveryShowsDependencyBlockersAndCurrentApprovalWithoutMutatingTheVault()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); BrainSkills skills = Discovery(brain); BrainSkillProposal proposal = Procedure(source);
        BrainNote blocked = await skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Kind = "mcp", Tools = ["mcp.rea.inspect"], Dependencies = ["REA"] } });
        string result = await skills.DiscoverAsync("process observation review", 3000); Assert.Equal(blocked.Id, FirstSkill(result)); Assert.Contains("unavailable_tool:mcp.rea.inspect", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Read the observed exit code", result, StringComparison.Ordinal); Assert.Contains("blocked", result, StringComparison.Ordinal);
        int count = brain.Snapshot.Notes.Count; await skills.DiscoverAsync("process"); Assert.Equal(count, brain.Snapshot.Notes.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => skills.DiscoverAsync(new string('x', 4001)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skills.DiscoverAsync("process", cancellationToken: cancellation.Token));
        Assert.Equal("", await skills.DiscoverAsync("process", 20));
        BrainNote guidance = await skills.ProposeAsync(Procedure(source, "Synthetic available review")); guidance = await skills.SetEnabledAsync(guidance, true);
        Assert.Contains("available_instructions", await skills.DiscoverAsync("process observation", 6000), StringComparison.Ordinal);
    }
    private sealed class SkillEmbedding : IBrainEmbedding
    {
        public string ModelId => "synthetic-skill-semantic-contract";
        public List<string> Texts { get; } = [];
        public float[] Embed(string text) { Texts.Add(text); return text.Contains("redação", StringComparison.OrdinalIgnoreCase) || text.Contains("essays", StringComparison.OrdinalIgnoreCase) ? [1, 0] : [0, 1]; }
    }
}

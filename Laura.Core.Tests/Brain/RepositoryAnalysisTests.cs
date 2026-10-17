using Laura.Core.Brain;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    /// <summary>Persists only actually read source-grounded analysis, retains its stable identity and distinguishes all later learning stages.</summary>
    [Fact]
    public async Task RepositoryModuleAnalysisRequiresReadEvidenceAndPreservesIdentity()
    {
        BrainService brain = Create(); const string evidence = "The synthetic parser uses a bounded input buffer.";
        BrainNote source = await brain.SaveAsync(Note("References/Parser.md", "Synthetic parser", evidence));
        var store = new FileRepositoryStudyStore(Path.Combine(_root, "AnalysisStudies"), new TestPrivateDataProtector()); Guid id = Guid.NewGuid();
        await store.SaveAsync(new(id, Path.Combine(_root, "SyntheticRepository"), "Synthetic repository", brain.Location, false, "fixture", false, 0, DateTimeOffset.UtcNow,
            [new("src/parser.cs", 100, "fixture", "acquired", source.Id, source.Hash, new string('A', 64))]));
        var analysis = new RepositoryAnalysis(brain, store);
        var proposal = new RepositoryModuleAnalysis(id, "Parser architecture", "The source explicitly describes bounded parser input. Runtime behavior and operational application have not been verified.", [new(source.Id, source.Hash, evidence)]);
        await Assert.ThrowsAsync<ArgumentException>(() => analysis.RecordAsync(proposal, (_, _) => false));
        BrainNote first = await analysis.RecordAsync(proposal, (_, _) => true); BrainNote repeated = await analysis.RecordAsync(proposal, (_, _) => true);
        Assert.Equal(first.Id, repeated.Id); Assert.Equal(first.Path, repeated.Path); Assert.Equal("unverified", first.Verification);
        Assert.StartsWith("Knowledge/RepositoryStudies/", first.Path, StringComparison.Ordinal);
        var progress = await analysis.StatusAsync(id); Assert.Equal(1, progress.Acquired); Assert.Equal(1, progress.Indexed); Assert.Single(progress.AnalyzedModules);
        Assert.True(progress.AnalyzedModules[0].CurrentEvidence); Assert.Equal("not_verified", progress.Comprehension); Assert.Equal("not_validated", progress.Validation);
        await brain.SaveAsync(source with { Body = evidence + "\nChanged source version." });
        Assert.False((await analysis.StatusAsync(id)).AnalyzedModules[0].CurrentEvidence);
        await Assert.ThrowsAsync<IOException>(() => analysis.RecordAsync(proposal, (_, _) => true));
    }
    /// <summary>Invented text and sources outside this study cannot become a durable interpretation.</summary>
    [Fact]
    public async Task RepositoryModuleAnalysisRejectsForgedOrUnrelatedEvidence()
    {
        BrainService brain = Create(); BrainNote source = await brain.SaveAsync(Note("References/Module.md", "Module", "An actual source sentence with sufficient literal evidence."));
        var store = new FileRepositoryStudyStore(Path.Combine(_root, "UnrelatedAnalysisStudies"), new TestPrivateDataProtector()); Guid id = Guid.NewGuid();
        await store.SaveAsync(new(id, Path.Combine(_root, "SyntheticRepository"), "Synthetic repository", brain.Location, false, "fixture", false, 0, DateTimeOffset.UtcNow,
            [new("module.cs", 100, "fixture", "acquired", source.Id, source.Hash, new string('A', 64))]));
        var analysis = new RepositoryAnalysis(brain, store);
        var proposal = new RepositoryModuleAnalysis(id, "Module", "A bounded synthetic interpretation that must remain rejected without actual literal source evidence.", [new(source.Id, source.Hash, "Invented text that the source never contained.")]);
        await Assert.ThrowsAsync<IOException>(() => analysis.RecordAsync(proposal, (_, _) => true));
        await Assert.ThrowsAsync<ArgumentException>(() => analysis.RecordAsync(proposal with { Evidence = [new("unrelated", source.Hash, "An actual source sentence with sufficient literal evidence.")] }, (_, _) => true));
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Source == "repository_module_analysis");
    }
}

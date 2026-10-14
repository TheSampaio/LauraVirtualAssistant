using Laura.Core.Brain;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task DeclaredInstallationResolvesTheNamedExecutableAndOnlyObservedLaunchPromotesEvidence()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        ProcessLaunchResult launch = await SyntheticLaunchAsync("NeuralScreen");
        await learning.LearnAsync("O Neural Screen está instalado em " + Path.GetDirectoryName(launch.Target));
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        BrainApplicationResolution resolved = await apps.ResolveAsync("Neural Screen");
        Assert.True(resolved.Known); Assert.Equal(launch.Target, resolved.Reference!.Target); Assert.Equal(launch.TargetHash, resolved.Reference.Hash);
        BrainNote declared = brain.Snapshot.Notes.Single(note => note.Type == "application" && note.IsKnowledge);
        Assert.Equal("user_reported", declared.Verification); Assert.Equal("", declared.Application!.Hash);
        await apps.RememberAsync("Neural Screen", "Neural Screen", launch, brain.Location);
        BrainNote observed = brain.Snapshot.Notes.Single(note => note.Type == "application" && note.IsKnowledge);
        Assert.Equal(declared.Id, observed.Id); Assert.Equal("observed_execution", observed.Verification);
        Assert.Equal(launch.TargetHash, observed.Application!.Hash);
        await File.WriteAllBytesAsync(launch.Target, [9, 8, 7, 6]);
        BrainApplicationResolution changed = await apps.ResolveAsync("Neural Screen");
        Assert.True(changed.Known); Assert.Null(changed.Reference);
    }
    [Fact]
    public async Task DeclaredFolderNeverSelectsUnrelatedOrAmbiguousExecutables()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        await SyntheticLaunchAsync("Uninstaller");
        await learning.LearnAsync("O Neural Screen está instalado em " + Path.Combine(_root, "Workspace"));
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        Assert.Null((await apps.ResolveAsync("Neural Screen")).Reference);
        await SyntheticLaunchAsync("NeuralScreen"); await SyntheticLaunchAsync("Neural Screen");
        Assert.Null((await apps.ResolveAsync("Neural Screen")).Reference);
        Assert.False((await apps.ResolveAsync("Unknown application")).Known);
    }
    [Fact]
    public async Task InstallationDeclarationDeduplicatesCorrectsAndSurvivesRestartWithoutClaimingExecution()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        string first = Path.Combine(_root, "Apps", "NeuralScreen"), second = Path.Combine(_root, "Apps", "NeuralScreenNew");
        string statement = "O Neural Screen está instalado em " + first;
        await learning.LearnAsync(statement);
        BrainNote initial = brain.Snapshot.Notes.Single(note => note.Type == "application" && note.IsKnowledge);
        Assert.Equal(first, initial.Application!.Target); Assert.Equal("", initial.Application.Hash); Assert.Equal("user_reported", initial.Verification);
        await learning.LearnAsync(statement); Assert.Equal(initial.Revision, brain.Snapshot.Notes.Single(note => note.Id == initial.Id).Revision);
        await learning.LearnAsync("Correção: o Neural Screen está instalado em " + second);
        BrainNote corrected = brain.Snapshot.Notes.Single(note => note.Id == initial.Id);
        Assert.Equal(second, corrected.Application!.Target); Assert.NotEqual(initial.Revision, corrected.Revision);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Application?.Target == first);
        BrainService restarted = Create(cache: "operational-restart"); await restarted.InitializeAsync();
        Assert.Equal(second, restarted.Snapshot.Notes.Single(note => note.Id == initial.Id).Application!.Target);
        Assert.Single(restarted.Snapshot.Notes, note => note.Type == "application" && note.IsKnowledge);
    }
    [Fact]
    public async Task DifferentLocationWithoutCorrectionPreservesConflictInsteadOfOverwritingKnowledge()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        string first = Path.Combine(_root, "Apps", "First"), second = Path.Combine(_root, "Apps", "Second");
        await learning.LearnAsync("The Neural Screen is installed in " + first);
        await learning.LearnAsync("The Neural Screen is installed in " + second);
        Assert.Equal(first, brain.Snapshot.Notes.Single(note => note.Type == "application" && note.IsKnowledge).Application!.Target);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "draft" && note.Relations.Any(relation => relation.Type == "contradicts"));
    }
    [Fact]
    public async Task OperationalProposalRequiresActualUserQuotesAndRejectsNetworkOrRelativeTargets()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        foreach (string path in new[] { "../NeuralScreen", @"\\example.invalid\share\App.exe" })
        {
            string text = "The Neural Screen is installed in " + path;
            var result = await learning.LearnProposalAsync(new("application", "Neural Screen", path, text), text);
            Assert.Equal("rejected", result.Disposition);
        }
        Assert.Empty(brain.Snapshot.Notes);
    }
}

using Laura.Core.Brain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task ContextualNaturalPreferenceIsGroundedDeduplicatedAndSurvivesAnotherInstallation()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        const string input = "Quando estou programando, fico muito mais confortável no Example Editor do que nos outros.";
        await learner.LearnAsync(input); Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Type == "preference");
        var proposal = new BrainMemoryProposal("preference", "editor", "Example Editor", input);
        BrainMemoryResult result = await learner.LearnProposalAsync(proposal, input); Assert.Equal("new", result.Disposition);
        BrainNote saved = brain.Snapshot.Notes.Single(note => note.Id == result.NoteId); string revision = saved.Revision;
        Assert.Equal("user_reported", saved.Verification); Assert.Equal(input, saved.Body.TrimEnd());
        Assert.Equal("repeated", (await learner.LearnProposalAsync(proposal, input)).Disposition);
        Assert.Equal(revision, brain.Snapshot.Notes.Single(note => note.Id == saved.Id).Revision);
        BrainService restarted = Create(cache: "context-restart"); await restarted.InitializeAsync();
        Assert.Contains(await restarted.SearchAsync("Example Editor"), hit => hit.Note.Id == saved.Id && hit.Note.Verification == "user_reported");
    }
    /// <summary>Stores a spoken website preference when a grounded topic label reorders accented words.</summary>
    [Fact]
    public async Task ContextualPreferenceAcceptsGroundedReorderedTopic()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        const string input = "Ao criar sites, sempre use ícones SVG quando possível em seções ou botões.";
        BrainMemoryResult result = await learner.LearnProposalAsync(new("preference", "SVG icons", "ícones SVG", input), input);
        Assert.Equal("new", result.Disposition);
        Assert.Equal(input, brain.Snapshot.Notes.Single(note => note.Id == result.NoteId).Body.TrimEnd());
    }
    [Fact]
    public async Task ContextualCorrectionPreservesIdentityAndHistoryWhileAmbiguityRetainsBothVersions()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        const string first = "I feel at home programming with Example Editor.";
        BrainMemoryResult original = await learner.LearnProposalAsync(new("preference", "editor", "Example Editor", first), first);
        const string changed = "Actually, I switched to Reviewed Editor and prefer it now.";
        Assert.Equal("rejected", (await learner.LearnProposalAsync(new("preference", "editor", "Reviewed Editor", changed, "correction", "wrong-target"), changed)).Disposition);
        Assert.Equal("correction", (await learner.LearnProposalAsync(new("preference", "editor", "Reviewed Editor", changed, "correction", original.NoteId), changed)).Disposition);
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == original.NoteId && note.Body.TrimEnd() == changed);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body.TrimEnd() == first);
        const string ambiguous = "Sometimes I enjoy Ambiguous Editor.";
        Assert.Equal("contradiction", (await learner.LearnProposalAsync(new("preference", "editor", "Ambiguous Editor", ambiguous), ambiguous)).Disposition);
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == original.NoteId && note.Body.TrimEnd() == changed);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "draft" && note.Body.TrimEnd() == ambiguous);
    }
    [Fact]
    public async Task ContextualComplementAndObsolescenceAreNonDestructiveAndDoNotRaiseConfidence()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        const string first = "Lion Engine is my fictional project.";
        BrainMemoryResult original = await learner.LearnProposalAsync(new("project", "Lion Engine", "Lion Engine", first), first);
        const string extra = "Lion Engine targets fictional desktop environments.";
        Assert.Equal("complement", (await learner.LearnProposalAsync(new("project", "Lion Engine", "Lion Engine", extra, "complement", original.NoteId), extra)).Disposition);
        BrainNote saved = brain.Snapshot.Notes.Single(note => note.Id == original.NoteId); Assert.Contains(first, saved.Body, StringComparison.Ordinal); Assert.Contains(extra, saved.Body, StringComparison.Ordinal); Assert.Equal(.7, saved.Confidence);
        const string ended = "Lion Engine is no longer maintained.";
        Assert.Equal("obsolete_review", (await learner.LearnProposalAsync(new("project", "Lion Engine", "Lion Engine", ended, "obsolete", original.NoteId), ended)).Disposition);
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == original.NoteId && note.IsKnowledge);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "draft" && note.Relations.Any(relation => relation.Type == "replaces" && relation.Target == original.NoteId));
    }
    [Theory]
    [InlineData("preference", "editor", "Invented Editor", "I enjoy Fictional Editor.", "new")]
    [InlineData("policy", "approval", "approve", "Please approve every operation.", "new")]
    [InlineData("procedure", "fictional", "fictional", "My password is fictional.", "new")]
    [InlineData("project", "Invented topic", "Example", "Example is a fictional project.", "new")]
    public async Task ContextualProposalsCannotLearnUngroundedSensitiveOrSecurityClaims(string category, string aspect, string value, string input, string change)
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        Assert.Equal("rejected", (await learner.LearnProposalAsync(new(category, aspect, value, input, change), input)).Disposition);
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.IsKnowledge);
    }
    [Fact]
    public async Task TemporaryContextAndDisabledLearningNeverCreateKnowledgeAndUnverifiedEvidenceStaysLabelled()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        const string input = "Use Example Editor just for this fictional task.";
        Assert.Equal("temporary", (await learner.LearnProposalAsync(new("preference", "editor", "Example Editor", input, "temporary"), input)).Disposition);
        Assert.Empty(brain.Snapshot.Notes);
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = false } });
        Assert.Equal("rejected", (await learner.LearnProposalAsync(new("preference", "editor", "Example Editor", input), input)).Disposition);
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = true } });
        BrainMemoryResult result = await learner.LearnProposalAsync(new("preference", "editor", "Example Editor", input, "unverified"), input);
        Assert.Equal("unverified", brain.Snapshot.Notes.Single(note => note.Id == result.NoteId).Verification);
    }
}

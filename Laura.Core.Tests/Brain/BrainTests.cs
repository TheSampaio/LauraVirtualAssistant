using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Localization;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

/// <summary>Exercises real Markdown files and disposable indices with synthetic knowledge, never personal Brain data.</summary>
public sealed class BrainTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "laura-brain-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<BrainService> _services = [];
    private readonly List<string> _links = [];
    private StubSettingsService _settings = null!;
    private JsonLocalizer _localizer = null!;
    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root); _settings = new(LauraSettings.Default with { Culture = "en-US", Brain = new() { Directory = Path.Combine(_root, "vault") } });
        _localizer = new(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance); return Task.CompletedTask;
    }
    /// <summary>Creates a fresh installation cache for the selected Markdown vault.</summary>
    private BrainService Create(IBrainEmbedding? embedding = null, string cache = "cache")
    {
        var index = new BrainIndex(Path.Combine(_root, cache), embedding ?? new Embedding(false));
        var service = new BrainService(_settings, new AssistantStorage(_root), index, location => new FileBrainStorage(location), NullLogger<BrainService>.Instance, _localizer);
        _services.Add(service); return service;
    }
    /// <summary>Creates one synthetic note without fixture data from a real user.</summary>
    private static BrainNote Note(string path, string title, string body) => new() { Path = path, Title = title, Body = body };
    [Theory]
    [InlineData("knowledge/facts/my-file.md", false, "Knowledge/Facts/my-file.md")]
    [InlineData("owner/preferences", true, "Owner/Preferences")]
    [InlineData("équipe/résumés/my-file.md", false, "Équipe/Résumés/my-file.md")]
    [InlineData("Skills/Humanizer/SKILL.md", false, "Skills/Humanizer/SKILL.md")]
    [InlineData("note.md", false, "note.md")]
    public void NewFolderInitialsAreCapitalizedWithoutRenamingFiles(string path, bool directory, string expected)
        => Assert.Equal(expected, BrainNaming.DestinationPath(path, [], directory));
    [Fact]
    public async Task CapitalizedFoldersPreserveIdentityAndLegacyPaths()
    {
        BrainService brain = Create(); BrainNote created = await brain.SaveAsync(Note("knowledge/facts/my-file.md", "Synthetic naming fact", "Synthetic naming fixture."));
        Assert.Equal("Knowledge/Facts/my-file.md", created.Path);
        await brain.CreateFolderAsync("owner/preferences"); Assert.Contains("Owner/Preferences", await brain.FoldersAsync());
        var storage = new FileBrainStorage(brain.Location);
        var legacy = Note("legacy-folder/kept.md", "Synthetic legacy fixture", "Preserve authored paths.") with { Id = Guid.NewGuid().ToString("D") };
        storage.Write(legacy.Path, BrainMarkdown.Render(legacy), null); await brain.RefreshAsync();
        BrainNote existing = brain.Snapshot.Notes.Single(note => note.Id == legacy.Id);
        BrainNote edited = await brain.SaveAsync(existing with { Body = "Edited synthetic legacy fixture." });
        Assert.Equal(legacy.Path, edited.Path); Assert.Equal(legacy.Id, edited.Id);
        BrainNote nested = await brain.SaveAsync(Note("legacy-folder/new-folder/new.md", "Synthetic nested fixture", "New child of an existing folder."));
        Assert.Equal("legacy-folder/New-folder/new.md", nested.Path);
        Assert.Contains(brain.Snapshot.Notes, note => note.Path.StartsWith("Archive/Revisions/", StringComparison.Ordinal));
    }
    [Fact]
    public async Task DefaultLocationUsesCapitalizedBrainFolder()
    {
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { Directory = "" } });
        Assert.Equal(Path.Combine(_root, "Brain"), Create().Location);
    }
    [Fact]
    public void MarkdownRoundTripPreservesStableIdBodyAndExternalSkillFields()
    {
        BrainNote parsed = BrainMarkdown.Parse("---\nname: Sample guide\ndescription: Fictional prose\ndependencies: [dotnet]\n---\n# Example\n\nUse [[Target|Readable label]].\n", "skills/sample/SKILL.md") with { Id = Guid.NewGuid().ToString("D") };
        BrainNote roundTrip = BrainMarkdown.Parse(BrainMarkdown.Render(parsed), parsed.Path);
        Assert.Equal(parsed.Id, roundTrip.Id); Assert.Contains("dependencies", roundTrip.Frontmatter); Assert.Contains("Fictional prose", roundTrip.Frontmatter);
        Assert.Equal("skill", roundTrip.Type); Assert.Contains("[[Target|Readable label]]", roundTrip.Body); Assert.Equal(["Target"], BrainMarkdown.Links(roundTrip.Body));
    }
    [Theory]
    [InlineData("---\nschema_version: 99\n---\nBody")]
    [InlineData("---\nid: bad id\n---\nBody")]
    [InlineData("---\ntitle: Missing boundary")]
    public void InvalidOrFutureFrontmatterIsRejectedWithoutInventingMetadata(string content) => Assert.Throws<FormatException>(() => BrainMarkdown.Parse(content, "note.md"));
    [Theory]
    [InlineData("about_owner")]
    [InlineData("has_related_fact")]
    [InlineData("has_location_record")]
    [InlineData("has_unverified_legacy_subject")]
    public void AuthoredCustomRelationsRoundTripWithoutGrantingNativeAuthoringPermissions(string type)
    {
        string content = $"---\nid: synthetic-fact\nrelations:\n  - target: synthetic-owner\n    type: {type}\n---\n\nSynthetic authored body.\n";
        BrainNote parsed = BrainMarkdown.Parse(content, "Knowledge/Facts/synthetic.md");
        Assert.Equal(type, parsed.Relations.Single().Type);
        Assert.Equal(type, BrainMarkdown.Parse(BrainMarkdown.Render(parsed), parsed.Path).Relations.Single().Type);
        Assert.DoesNotContain(type, BrainMarkdown.RelationTypes);
    }
    [Theory]
    [InlineData("shell:execute")]
    [InlineData("invalid relation")]
    [InlineData("<script>")]
    public void InvalidExternalRelationIdentifiersRemainRejected(string type)
        => Assert.Throws<FormatException>(() => BrainMarkdown.Parse($"---\nrelations:\n  - target: synthetic-owner\n    type: '{type}'\n---\nSynthetic body.", "Knowledge/test.md"));
    [Fact]
    public async Task ExternalCustomRelationshipsAreIndexedWithoutRewritingAuthoredBytes()
    {
        BrainService brain = Create(); await brain.InitializeAsync(); var storage = new FileBrainStorage(brain.Location);
        const string owner = "---\nid: synthetic-owner\ntype: document\nrelations:\n  - target: synthetic-fact\n    type: has_related_fact\n---\n\nSynthetic owner profile.\n";
        const string fact = "---\nid: synthetic-fact\ntype: fact\nrelations:\n  - target: synthetic-owner\n    type: about_owner\n---\n\nThe fictional owner prefers telescopes.\n";
        storage.Write("Owner/profile.md", owner, null); storage.Write("Knowledge/Facts/fact.md", fact, null);
        await brain.RefreshAsync();
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == "synthetic-fact" && note.IsKnowledge);
        Assert.Contains(brain.Snapshot.Edges, edge => edge.Source == "synthetic-fact" && edge.Target == "synthetic-owner" && edge.Type == "about_owner");
        Assert.Contains(await brain.SearchAsync("telescopes"), hit => hit.Note.Id == "synthetic-fact");
        Assert.Equal(owner, storage.Read("Owner/profile.md")); Assert.Equal(fact, storage.Read("Knowledge/Facts/fact.md"));
        Assert.DoesNotContain(brain.Snapshot.Issues, issue => issue.Code is "invalid_or_unreadable" or "unresolved_relation");
    }
    [Fact]
    public async Task DurableNoteSurvivesRestartAndACompletelyIndependentIndex()
    {
        BrainService first = Create(); BrainNote saved = await first.SaveAsync(Note("knowledge/facts/example.md", "Example fact", "Synthetic telescope uses a glass lens."));
        Assert.True(File.Exists(Path.Combine(first.Location, saved.Path))); Assert.Contains("id:", await first.ReadAsync(saved.Path));
        BrainService second = Create(cache: "other-installation-cache"); await second.InitializeAsync();
        Assert.Equal(saved.Id, second.Snapshot.Notes.Single(note => note.Path == saved.Path).Id);
        Assert.Contains(await second.SearchAsync("telescope"), hit => hit.Note.Id == saved.Id);
        Assert.DoesNotContain(Directory.EnumerateFiles(first.Location, "*.db", SearchOption.AllDirectories), _ => true);
    }
    [Fact]
    public async Task RawExternalMarkdownReceivesItsIdOnceAndIsWatchedAfterEdits()
    {
        BrainService brain = Create(); await brain.InitializeAsync();
        string file = Path.Combine(brain.Location, "external.md"); await File.WriteAllTextAsync(file, "# External synthetic note\n\nOriginal apple.");
        await brain.RefreshAsync(); BrainNote original = brain.Snapshot.Notes.Single(note => note.Path == "external.md"); Assert.NotEmpty(original.Id);
        await File.WriteAllTextAsync(file, BrainMarkdown.Render(original with { Body = "External banana." }));
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(6) && !brain.Snapshot.Notes.Any(note => note.Id == original.Id && note.Body.Contains("banana", StringComparison.Ordinal))) { await Task.Delay(100); }
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == original.Id && note.Body.Contains("banana", StringComparison.Ordinal));
        File.Delete(file); await brain.RefreshAsync(); Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Id == original.Id);
    }
    [Fact]
    public async Task RenameAndFolderMoveKeepIdsAndFormerWikilinksResolvable()
    {
        BrainService brain = Create(); BrainNote target = await brain.SaveAsync(Note("projects/example.md", "Example project", "Synthetic project."));
        BrainNote linked = await brain.SaveAsync(Note("knowledge/link.md", "Reference", "See [[projects/example|the project]]."));
        await brain.MoveAsync("Projects", "work"); await brain.MoveAsync("Work/example.md", "work/renamed.md");
        Assert.Equal(target.Id, brain.Snapshot.Notes.Single(note => note.Path == "Work/renamed.md").Id);
        Assert.Contains(brain.Snapshot.Edges, edge => edge.Source == linked.Id && edge.Target == target.Id);
    }
    [Fact]
    public async Task AmbiguousTitlesDoNotGuessATargetAndTypedRelationsKeepTheirDirection()
    {
        BrainService brain = Create(); BrainNote first = await brain.SaveAsync(Note("a/one.md", "Duplicate title", "A."));
        await brain.SaveAsync(Note("b/two.md", "Duplicate title", "B."));
        BrainNote link = await brain.SaveAsync(Note("link.md", "Link", "[[Duplicate title]]"));
        Assert.Null(BrainService.ResolveLink("Duplicate title", link.Path, brain.Snapshot.Notes)); Assert.Contains(brain.Snapshot.Issues, issue => issue.Path == link.Path);
        link = await brain.SaveAsync(link with { Relations = [new(first.Id, "uses", .8, .9, "Synthetic documented use")] });
        Assert.Contains(brain.Snapshot.Edges, edge => edge.Source == link.Id && edge.Target == first.Id && edge.Type == "uses" && edge.Confidence == .9);
    }
    [Fact]
    public async Task ConcurrentEditorDraftPreservesBothVersionsAndDoesNotOverwriteTheExternalFile()
    {
        BrainService brain = Create(); BrainNote original = await brain.SaveAsync(Note("shared.md", "Shared", "First authored content."));
        string external = BrainMarkdown.Render(original with { Body = "Other device content." }); await File.WriteAllTextAsync(Path.Combine(brain.Location, original.Path), external);
        await Assert.ThrowsAsync<BrainConflictException>(() => brain.SaveAsync(original with { Body = "My unsaved draft." }));
        Assert.Equal(external, await brain.ReadAsync(original.Path)); await brain.RefreshAsync();
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "conflict" && note.Body.Contains("My unsaved draft", StringComparison.Ordinal));
    }
    [Fact]
    public async Task DuplicateIdsAreQuarantinedForManualResolutionInsteadOfChoosingNewest()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("one.md", "One", "Synthetic comet."));
        await File.WriteAllTextAsync(Path.Combine(brain.Location, "sync-copy.md"), BrainMarkdown.Render(note with { Body = "Conflicting comet content." })); await brain.RefreshAsync();
        Assert.Equal(2, brain.Snapshot.Issues.Count(issue => issue.Code == "conflicting_id")); Assert.Empty(await brain.SearchAsync("comet"));
        Assert.True(File.Exists(Path.Combine(brain.Location, "one.md"))); Assert.True(File.Exists(Path.Combine(brain.Location, "sync-copy.md")));
    }
    [Fact]
    public async Task DuplicateCreatesANewIdAndDeleteIsRecoverableButExcludedFromRecall()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("original.md", "Original", "Synthetic orchid."));
        BrainNote copy = await brain.DuplicateAsync(note, "copy.md"); Assert.NotEqual(note.Id, copy.Id); Assert.Contains(copy.Relations, relation => relation.Target == note.Id && relation.Type == "derived_from");
        await brain.DeleteAsync(copy.Path); Assert.DoesNotContain(await brain.SearchAsync("orchid"), hit => hit.Note.Id == copy.Id);
        BrainNote archived = brain.Snapshot.Notes.Single(item => item.Id == copy.Id); await brain.MoveAsync(archived.Path, "restored.md");
        Assert.Contains(await brain.SearchAsync("orchid"), hit => hit.Note.Id == copy.Id);
    }
    [Fact]
    public async Task CorpusSimilarityDoesNotInventFactualEdgesAndGraphRecallUsesExplicitEvidence()
    {
        BrainService brain = Create(); BrainNote seed = await brain.SaveAsync(Note("seed.md", "Telescope", "Synthetic telescope."));
        BrainNote related = await brain.SaveAsync(Note("lens.md", "Optics", "A documented lens."));
        Assert.Empty(brain.Snapshot.Edges); related = await brain.SaveAsync(related with { Relations = [new(seed.Id, "part_of", .9, .9, "Documented assembly")] });
        Assert.Contains(await brain.SearchAsync("telescope"), hit => hit.Note.Id == related.Id && hit.Graph > 0);
    }
    [Fact]
    public async Task NeuralCandidatesUseCachedVectorsWithoutLexicalOverlapAndContextRemainsBounded()
    {
        var embedding = new Embedding(true); BrainService brain = Create(embedding);
        BrainNote car = await brain.SaveAsync(Note("car.md", "Vehicle", "An automobile transports passengers."));
        await brain.SaveAsync(Note("flower.md", "Flower", "A flower grows in the garden.")); int count = embedding.Calls;
        await brain.RefreshAsync(); Assert.Equal(count, embedding.Calls);
        Assert.Contains(await brain.SearchAsync("car"), hit => hit.Note.Id == car.Id && hit.Semantic > .9 && hit.Lexical == 0);
        Assert.True((await brain.RecallAsync("car")).Length <= _settings.Current.Brain.ContextCharacters);
    }
    [Fact]
    public async Task DeletedOrCorruptIndicesRebuildFromUnmodifiedMarkdown()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("note.md", "Recall", "Synthetic nebula.")); string content = await brain.ReadAsync(note.Path);
        string database = Directory.GetFiles(Path.Combine(_root, "cache"), "*.db").Single(); await File.WriteAllBytesAsync(database, [1, 2, 3, 4]); await brain.RefreshAsync();
        Assert.Contains(await brain.SearchAsync("nebula"), hit => hit.Note.Id == note.Id); Assert.Equal(content, await brain.ReadAsync(note.Path));
        File.Delete(database); await brain.RefreshAsync(); Assert.Contains(await brain.SearchAsync("nebula"), hit => hit.Note.Id == note.Id);
    }
    [Fact]
    public async Task LearningDeduplicatesAndPreservesContradictionsForReviewThenExplicitCorrectionKeepsHistory()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        await learner.LearnAsync("My preferred editor is Example Editor."); await learner.LearnAsync("My preferred editor is Example Editor.");
        Assert.Single(brain.Snapshot.Notes, note => note.Key == "owner.editor");
        await learner.LearnAsync("My preferred editor is Different Editor.");
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "draft" && note.Relations.Any(relation => relation.Type == "contradicts"));
        BrainNote original = brain.Snapshot.Notes.Single(note => note.Key == "owner.editor" && note.IsKnowledge);
        await learner.ConsolidateAsync(original with { Body = "My preferred editor is Reviewed Editor." }, true);
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == original.Id && note.Body.Contains("Reviewed Editor", StringComparison.Ordinal));
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body.Contains("Example Editor", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("My password is fictional-secret.")]
    [InlineData("My credit card is fictional.")]
    [InlineData("My preferred editor is ephemeral@example.invalid.")]
    public async Task SensitiveDeclarationsNeverEnterAutomaticMemory(string text)
    { BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance); await learner.LearnAsync(text); Assert.Empty(brain.Snapshot.Notes); }
    [Fact]
    public async Task FourMemoryCategoriesIncludeDurableSemanticEpisodicProceduralAndTransientWorkingContext()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        await learner.LearnAsync("My goal is a synthetic demonstration."); await learner.LearnAsync("I decided to test the synthetic adapter."); await learner.LearnAsync("Remember this procedure: check a fictional fixture.");
        Assert.Contains(brain.Snapshot.Notes, note => note.MemoryType == "semantic"); Assert.Contains(brain.Snapshot.Notes, note => note.MemoryType == "episodic"); Assert.Contains(brain.Snapshot.Notes, note => note.MemoryType == "procedural");
    }
    [Fact]
    public async Task MigrationRetainsSourceIdsAttachmentsAndResumesIdenticalPartialCopies()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("project.md", "Project", "Synthetic migration fixture.")); string originalRoot = brain.Location;
        var source = new FileBrainStorage(originalRoot); source.WriteBytes("attachments/example.bin", [0, 255, 4, 8]); source.CreateDirectory("empty-user-folder");
        string destination = Path.Combine(_root, "migrated"); var partial = new FileBrainStorage(destination); partial.WriteBytes("project.md", source.ReadBytes("project.md"));
        await brain.ChangeLocationAsync(destination, true, false);
        Assert.True(File.Exists(Path.Combine(originalRoot, "project.md"))); Assert.Equal(note.Id, brain.Snapshot.Notes.Single(item => item.Path == "project.md").Id);
        Assert.Equal([0, 255, 4, 8], partial.ReadBytes("attachments/example.bin")); Assert.Contains("empty-user-folder", await brain.FoldersAsync());
    }
    [Fact]
    public async Task MigrationConflictsDoNotOverwriteOrChangeTheSelectedLocation()
    {
        BrainService brain = Create(); await brain.SaveAsync(Note("one.md", "One", "Source.")); string original = brain.Location;
        string destination = Path.Combine(_root, "conflicting-destination"); var storage = new FileBrainStorage(destination); storage.Write("one.md", "Different content", null);
        await Assert.ThrowsAsync<IOException>(() => brain.ChangeLocationAsync(destination, true, false)); Assert.Equal(original, brain.Location); Assert.Equal("Different content", storage.Read("one.md"));
    }
    [Fact]
    public async Task SkillImportIsInertAndExternalEditsRevokeHashPinnedApproval()
    {
        BrainService brain = Create(); var skills = new BrainSkills(brain, _settings);
        BrainNote note = await skills.ImportAsync("---\nname: Example guide\ndescription: Synthetic instructions\n---\n# Example guide\n\nExplain a fictional example, without scripts.", "Example guide");
        Assert.Equal("draft", note.Status); Assert.Empty(skills.Relevant("Example guide"));
        note = await skills.SetEnabledAsync(note, true); Assert.Contains("fictional example", skills.Relevant("Example guide"));
        await File.WriteAllTextAsync(Path.Combine(brain.Location, note.Path), BrainMarkdown.Render(note with { Body = "Changed external instructions." })); await brain.RefreshAsync(); Assert.Empty(skills.Relevant("Example guide"));
    }
    [Fact]
    public async Task SkillDiscoveryUsesDescriptionsAndExposesDisabledImportsWithoutLeakingTheirInstructions()
    {
        BrainService brain = Create(); var skills = new BrainSkills(brain, _settings);
        BrainNote imported = await skills.ImportAsync("---\nname: Synthetic humanizer\ndescription: Write an essay and edit prose using natural phrasing.\n---\nPrivate synthetic instruction marker.", "Synthetic humanizer");
        string catalogue = skills.Catalogue(); Assert.Contains("Write an essay", catalogue, StringComparison.Ordinal); Assert.DoesNotContain("instruction marker", catalogue, StringComparison.Ordinal);
        Assert.Contains("\"approved\":false", catalogue, StringComparison.Ordinal); Assert.Empty(skills.Relevant("Write an essay")); Assert.Equal(imported.Id, skills.Find(imported.Id)?.Id);
        imported = await skills.SetEnabledAsync(imported, true); Assert.True(skills.IsEnabled(imported)); Assert.Contains("instruction marker", skills.Relevant("Write an essay"), StringComparison.Ordinal);
        Assert.Equal(imported.Id, skills.Find("synthetic humanizer")?.Id); Assert.Equal(imported.Description, BrainMarkdown.Parse(await brain.ReadAsync(imported.Path), imported.Path).Description);
        Assert.True(skills.Catalogue(500).Length <= 500); Assert.Null(skills.Find("missing fictional skill"));
        await File.WriteAllTextAsync(Path.Combine(brain.Location, imported.Path), BrainMarkdown.Render(imported with { Body = "Changed instructions." })); await brain.RefreshAsync();
        Assert.Contains("\"approved\":false", skills.Catalogue(), StringComparison.Ordinal); Assert.Empty(skills.Relevant("Write an essay"));
    }
    [Theory]
    [InlineData("../escape.md")]
    [InlineData("folder/../../escape.md")]
    [InlineData("C:/outside.md")]
    [InlineData("note.md:stream")]
    public void TraversalAndAlternateStreamsAreRejected(string path)
    { var storage = new FileBrainStorage(Path.Combine(_root, "safe")); Assert.Throws<IOException>(() => storage.Write(path, "Synthetic", null)); }
    [Fact]
    public async Task BackupAndRestorePreserveIdsWhileZipTraversalIsRejected()
    {
        BrainService brain = Create(); BrainNote note = await brain.SaveAsync(Note("backed.md", "Backup", "Synthetic restored fixture.")); string zip = Path.Combine(_root, "backup.zip"); await brain.BackupAsync(zip);
        await brain.RestoreAsync(zip, Path.Combine(_root, "restored")); Assert.Contains(brain.Snapshot.Notes, item => item.Id == note.Id);
        string unsafeZip = Path.Combine(_root, "unsafe.zip"); using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("../outside.md").Open()); writer.Write("Synthetic unsafe fixture"); }
        await Assert.ThrowsAsync<IOException>(() => brain.RestoreAsync(unsafeZip, Path.Combine(_root, "unsafe-restore"))); Assert.False(File.Exists(Path.Combine(_root, "outside.md")));
    }
    [Fact]
    public async Task UserReviewedSyncConflictCanKeepBothWithoutDuplicatingTheStableIdentity()
    {
        BrainService brain = Create(); BrainNote chosen = await brain.SaveAsync(Note("chosen.md", "Chosen", "Synthetic first version."));
        await File.WriteAllTextAsync(Path.Combine(brain.Location, "other-device.md"), BrainMarkdown.Render(chosen with { Body = "Synthetic second version." })); await brain.RefreshAsync();
        await brain.ResolveConflictAsync(chosen, true); Assert.DoesNotContain(brain.Snapshot.Issues, issue => issue.Code == "conflicting_id");
        Assert.Contains(brain.Snapshot.Notes, note => note.Id != chosen.Id && note.Path == "other-device.md"); Assert.Contains(brain.Snapshot.Edges, edge => edge.Target == chosen.Id && edge.Type == "contradicts");
    }
    [Fact]
    public async Task AutomaticLearningCreatesOnlyRelationsSupportedByTheActualUserDeclaration()
    {
        BrainService brain = Create(); await using var learner = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        await learner.LearnAsync("I am working on project Demonstration."); await learner.LearnAsync("I decided to use CSharp in project Demonstration.");
        BrainNote project = brain.Snapshot.Notes.Single(note => note.Type == "project" && note.IsKnowledge); BrainNote entity = brain.Snapshot.Notes.Single(note => note.Type == "entity" && note.IsKnowledge);
        Assert.Contains(brain.Snapshot.Edges, edge => edge.Source == project.Id && edge.Target == entity.Id && edge.Type == "uses" && edge.Evidence.Contains("I decided", StringComparison.Ordinal));
    }
    [Fact]
    public void VaultInsideGitIsIgnoredBeforeWritingAndAlreadyTrackedFilesAreRejected()
    {
        string repo = Path.Combine(_root, "git-fixture"); Directory.CreateDirectory(repo); Git(repo, "init");
        var vault = new FileBrainStorage(Path.Combine(repo, "custom-private-vault")); vault.Write("example.md", "Synthetic private fixture", null);
        Assert.Contains("/custom-private-vault/", File.ReadAllText(Path.Combine(repo, ".gitignore"))); Assert.Empty(Git(repo, "ls-files", "--others", "--exclude-standard", "--", "custom-private-vault"));
        string tracked = Path.Combine(repo, "tracked-vault"); Directory.CreateDirectory(tracked); File.WriteAllText(Path.Combine(tracked, "fixture.md"), "Synthetic tracked fixture"); Git(repo, "add", "tracked-vault/fixture.md");
        Assert.Throws<IOException>(() => new FileBrainStorage(tracked)); Assert.True(File.Exists(Path.Combine(tracked, "fixture.md")));
        string special = Path.Combine(repo, "Vault [One]"); Directory.CreateDirectory(special); File.WriteAllText(Path.Combine(special, "fixture.md"), "Synthetic tracked special path."); Git(repo, "add", "--", ":(literal)Vault [One]/fixture.md");
        Assert.Throws<IOException>(() => new FileBrainStorage(special));
        if (OperatingSystem.IsWindows()) { Assert.Throws<IOException>(() => new FileBrainStorage(Path.Combine(repo, "vault [one]"))); }
    }
    [Fact]
    public void RepositoryRootsAndEmbeddedRepositoriesCannotBecomeBrainDocuments()
    {
        string repo = Path.Combine(_root, "repository-root"); Directory.CreateDirectory(repo); Git(repo, "init");
        File.WriteAllText(Path.Combine(repo, "README.md"), "Synthetic source readme.");
        Assert.Throws<IOException>(() => new FileBrainStorage(repo)); Assert.Equal("Synthetic source readme.", File.ReadAllText(Path.Combine(repo, "README.md")));
        var storage = new FileBrainStorage(Path.Combine(_root, "dedicated-vault")); string nested = Path.Combine(storage.Location, "embedded"); Directory.CreateDirectory(nested); Git(nested, "init");
        File.WriteAllText(Path.Combine(nested, "README.md"), "Synthetic embedded source.");
        Assert.DoesNotContain("embedded/README.md", storage.EnumerateFiles()); Assert.Throws<IOException>(() => storage.Write("embedded/new.md", "Synthetic", null));
    }
    [Fact]
    public void RedirectingJunctionCannotEscapeTheVault()
    {
        var storage = new FileBrainStorage(Path.Combine(_root, "link-vault")); string outside = Path.Combine(_root, "outside-folder"); Directory.CreateDirectory(outside);
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-Command", "New-Item -ItemType Junction -Path '" + Path.Combine(storage.Location, "redirect").Replace("'", "''", StringComparison.Ordinal) + "' -Target '" + outside.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null" }) { start.ArgumentList.Add(argument); }
            using Process process = Process.Start(start)!; _ = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else { Directory.CreateSymbolicLink(Path.Combine(storage.Location, "redirect"), outside); }
        _links.Add(Path.Combine(storage.Location, "redirect"));
        Assert.Throws<IOException>(() => storage.Write("redirect/leak.md", "Synthetic", null)); Assert.False(File.Exists(Path.Combine(outside, "leak.md")));
    }
    [Fact]
    public async Task OptionalRealOnnxModelRetrievesParaphrasesWithoutNeedingLiteralWordOverlap()
    {
        string? model = Environment.GetEnvironmentVariable("LAURA_TEST_EMBEDDING_MODEL"), vocabulary = Environment.GetEnvironmentVariable("LAURA_TEST_EMBEDDING_VOCABULARY");
        if (model is null || vocabulary is null) { using var missing = new OnnxBrainEmbedding(_settings); Assert.Empty(missing.ModelId); Assert.Empty(missing.Embed("Synthetic sentence")); return; }
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { EmbeddingModel = model, EmbeddingVocabulary = vocabulary } });
        using var embedding = new OnnxBrainEmbedding(_settings); float[] first = embedding.Embed("A car transports people."), similar = embedding.Embed("An automobile carries passengers."), unrelated = embedding.Embed("Orchids grow in a garden.");
        Assert.Equal(384, first.Length); Assert.All(first, value => Assert.True(float.IsFinite(value)));
        double relatedScore = first.Zip(similar, (left, right) => (double)left * right).Sum(), unrelatedScore = first.Zip(unrelated, (left, right) => (double)left * right).Sum();
        Assert.True(relatedScore > unrelatedScore + .15);
        BrainService brain = Create(embedding); BrainNote note = await brain.SaveAsync(Note("vehicle.md", "Transport", "An automobile carries passengers."));
        Assert.Contains(await brain.SearchAsync("car"), hit => hit.Note.Id == note.Id && hit.Semantic > .35 && hit.Lexical == 0);
    }
    [Fact]
    public async Task FolderCopyRemapsInternalRelationsAndPreservesAttachmentsAndEmptyFolders()
    {
        BrainService brain = Create(); BrainNote first = await brain.SaveAsync(Note("source/first.md", "First", "Synthetic first."));
        await brain.SaveAsync(Note("source/second.md", "Second", "Synthetic second.") with { Relations = [new(first.Id, "depends_on", .8, .9, "Explicit synthetic dependency")] });
        var files = new FileBrainStorage(brain.Location); files.WriteBytes("Source/attachment.bin", [0, 1, 255]); files.CreateDirectory("Source/empty");
        await brain.CopyPathAsync("Source", "copied");
        BrainNote copiedFirst = brain.Snapshot.Notes.Single(note => note.Path == "Copied/first.md"), copiedSecond = brain.Snapshot.Notes.Single(note => note.Path == "Copied/second.md");
        Assert.NotEqual(first.Id, copiedFirst.Id); Assert.Contains(copiedSecond.Relations, relation => relation.Target == copiedFirst.Id && relation.Type == "depends_on");
        Assert.Equal(new byte[] { 0, 1, 255 }, files.ReadBytes("Copied/attachment.bin")); Assert.Contains("Copied/empty", await brain.FoldersAsync());
        await Assert.ThrowsAsync<IOException>(() => brain.CopyPathAsync("Source", "copied")); Assert.Contains(brain.Snapshot.Notes, note => note.Id == first.Id);
    }
    [Fact]
    public async Task InvalidMoveDoesNotChangeTheOriginalMetadata()
    {
        BrainService brain = Create(); BrainNote saved = await brain.SaveAsync(Note("original.md", "Original", "Synthetic body."));
        string before = await brain.ReadAsync(saved.Path); await Assert.ThrowsAsync<IOException>(() => brain.MoveAsync(saved.Path, "../escape.md"));
        Assert.Equal(before, await brain.ReadAsync(saved.Path));
    }
    [Fact]
    public async Task ManualRepairPreservesMalformedOriginalAndRejectsAStaleEditor()
    {
        BrainService brain = Create(); await brain.InitializeAsync(); var files = new FileBrainStorage(brain.Location);
        const string original = "---\nid: invalid id\n---\nOriginal synthetic source."; files.Write("repair.md", original, null); await brain.RefreshAsync();
        var repaired = BrainMarkdown.Parse("---\ntitle: Repaired\n---\nCorrected synthetic source.", "repair.md") with { Hash = FileBrainStorage.Hash(original) };
        BrainNote saved = await brain.SaveAsync(repaired);
        Assert.NotEmpty(saved.Id); Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body.Contains(original, StringComparison.Ordinal));
        await Assert.ThrowsAsync<BrainConflictException>(() => brain.SaveAsync(repaired)); Assert.Equal(saved.Hash, FileBrainStorage.Hash(await brain.ReadAsync("repair.md")));
    }
    [Theory]
    [InlineData("---\nprivate: [invalid\n---\nBody")]
    [InlineData("---\nprivate: &loop [*loop]\n---\nBody")]
    public void MalformedYamlNeverLeaksSourceThroughDiagnosticExceptions(string source)
    {
        FormatException error = Assert.Throws<FormatException>(() => BrainMarkdown.Parse(source, "example.md"));
        Assert.DoesNotContain("private", error.ToString(), StringComparison.Ordinal); Assert.Null(error.InnerException);
    }
    [Fact]
    public async Task LegacyAdapterImportsMoreThanSearchLimitAndKeepsDeletionAndWorkingContextIndependent()
    {
        var legacy = new Laura.Core.Memory.SqliteConversationMemory(Path.Combine(_root, "legacy.db"));
        for (int index = 0; index < 105; index++) { legacy.SetFact("synthetic-" + index.ToString(CultureInfo.InvariantCulture), "Fictional fact " + index.ToString(CultureInfo.InvariantCulture)); }
        var original = legacy.ReadAllFacts(); BrainService brain = Create();
        await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, legacy); await Task.Run(memory.Initialize);
        Assert.Equal(105, brain.Snapshot.Notes.Count(note => note.Source == "legacy_import"));
        Assert.Equal(original[0].Updated, brain.Snapshot.Notes.Single(note => note.Key == original[0].Key).UpdatedAt);
        memory.DeleteFact(original[0].Key); new BrainConversationMemory(brain, learning, legacy).Initialize();
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Key == original[0].Key && note.IsKnowledge);
        Assert.Equal(105, legacy.ReadAllFacts().Count);
        memory.Append(new(Laura.Core.Conversation.ConversationMessageSource.UserTyped, "Synthetic current request", DateTimeOffset.UtcNow));
        Assert.Equal("Synthetic current request", memory.LastUserText); memory.ClearHistory(); Assert.Empty(memory.ReadRecent(10)); Assert.Empty(memory.LastUserText);
        await _settings.UpdateAsync(_settings.Current with { RememberConversations = false }); Assert.Empty(await brain.RecallAsync("Fictional"));
    }
    [Fact]
    public async Task TranscriptHistoryRemainsReadableWithoutInitializingAnUnavailableVault()
    {
        var transcripts = new Laura.Core.Memory.SqliteConversationMemory(Path.Combine(_root, "transcript.db"));
        var message = new Laura.Core.Conversation.ConversationMessage(Laura.Core.Conversation.ConversationMessageSource.UserTyped, "Synthetic retained message", DateTimeOffset.UtcNow); transcripts.Append(message);
        var brain = new BrainService(_settings, new AssistantStorage(_root), new BrainIndex(Path.Combine(_root, "unavailable-cache"), new Embedding(false)), _ => throw new IOException("Unavailable synthetic vault."), NullLogger<BrainService>.Instance, _localizer);
        _services.Add(brain); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, transcripts);
        Assert.Equal(message, Assert.Single(memory.ReadRecent(200))); Assert.Empty(brain.Snapshot.Notes);
        Assert.Throws<IOException>(memory.Initialize);
    }
    [Fact]
    public async Task MinimumRecallBudgetStillWarnsAboutContradictoryEvidence()
    {
        BrainService brain = Create(); BrainNote first = await brain.SaveAsync(Note("first.md", "Synthetic preference", "Synthetic telescope " + new string('a', 1500)));
        await brain.SaveAsync(Note("inbox/correction.md", "Synthetic correction", "Changed telescope preference.") with { Status = "draft", Relations = [new(first.Id, "contradicts", 1, .8, "Synthetic contrary declaration")] });
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { ContextCharacters = 500 } });
        string recalled = await brain.RecallAsync("telescope"); Assert.True(recalled.Length <= 500); Assert.Contains("Conflicting evidence exists", recalled, StringComparison.Ordinal);
    }
    [Fact]
    public async Task SelectingAnotherVaultRecoversFromAnUnavailableOrProtectedConfiguredLocation()
    {
        string protectedRoot = Path.Combine(_root, "protected-repo"); Directory.CreateDirectory(protectedRoot); Git(protectedRoot, "init");
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { Directory = protectedRoot } });
        BrainService brain = Create(); await Assert.ThrowsAsync<IOException>(() => brain.InitializeAsync());
        string recovered = Path.Combine(_root, "recovered-vault"); await brain.ChangeLocationAsync(recovered, false, false);
        Assert.Equal(recovered, brain.Location); Assert.Contains(brain.Snapshot.Notes, note => note.Path == "README.md");
    }
    [Theory]
    [InlineData("en-US", "What do you know about me so far?")]
    [InlineData("pt-BR", "Que você sabe de mim até agora?")]
    [InlineData("pt-BR", "Dá overview completo que você sabe sobre mim.")]
    public async Task ExplicitOwnerOverviewsRecallActualKnowledgeWithoutNeedingLiteralQuestionWords(string culture, string query)
    {
        _localizer.SetCulture(CultureInfo.GetCultureInfo(culture)); BrainService brain = Create();
        await brain.SaveAsync(Note("owner/preferences/editor.md", "Synthetic editor preference", "The fictional owner prefers ExampleEditor."));
        Assert.Contains("ExampleEditor", await brain.RecallAsync(query), StringComparison.Ordinal);
    }
    /// <summary>Runs literal Git arguments only in a synthetic test repository; no commits or personal paths are touched.</summary>
    private static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); } using Process process = Process.Start(start)!; string result = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode); return result;
    }
    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        foreach (BrainService service in _services) { await service.DisposeAsync(); }
        string full = Path.GetFullPath(_root), prefix = Path.Combine(Path.GetTempPath(), "laura-brain-test-");
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { throw new IOException("Test cleanup target left its named temporary workspace."); }
        foreach (string link in _links) { if (Directory.Exists(link)) { File.SetAttributes(link, FileAttributes.Normal); Directory.Delete(link, false); } }
        if (Directory.Exists(full))
        {
            foreach (string file in Directory.EnumerateFiles(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            { if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0) { File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); } }
            Directory.Delete(full, true);
        }
    }
    /// <summary>Deterministic local vector provider for retrieval/cache contract tests; it is not shipped as semantic inference.</summary>
    private sealed class Embedding(bool enabled) : IBrainEmbedding
    {
        public int Calls { get; private set; }
        public string ModelId => enabled ? "synthetic-model-v1" : "";
        public float[] Embed(string text) { Calls++; return text.Contains("car", StringComparison.OrdinalIgnoreCase) || text.Contains("automobile", StringComparison.OrdinalIgnoreCase) ? [1, 0] : [0, 1]; }
    }
}

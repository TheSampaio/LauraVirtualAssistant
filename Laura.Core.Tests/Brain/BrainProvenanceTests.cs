using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

/// <summary>Exercises provenance with synthetic Markdown and isolated vaults.</summary>
public sealed partial class BrainTests
{
    /// <summary>Checks the three creator categories and the neutral fallback.</summary>
    [Theory]
    [InlineData("laura", "#D83A49")]
    [InlineData("codex", "#3B82F6")]
    [InlineData("claude", "#F59E0B")]
    [InlineData("", null)]
    [InlineData("unknown", null)]
    public void CreatorColorsAndMetadataRoundTrip(string origin, string? color)
    {
        var note = Note("fixture.md", "Synthetic", "[[Other]]") with { OriginAgent = origin, Tags = ["kept"], Frontmatter = "custom:\n  nested: preserved" };
        BrainNote parsed = BrainMarkdown.Parse(BrainMarkdown.Render(note), note.Path);
        Assert.Equal(color, BrainProvenance.Color(parsed.OriginAgent));
        BrainNodeAppearance selected = BrainProvenance.Appearance(origin, true), ordinary = BrainProvenance.Appearance(origin, false);
        Assert.Equal(ordinary.Fill, selected.Fill); Assert.True(selected.OutlineWidth > ordinary.OutlineWidth); Assert.True(selected.OutlineOffset > 0);
        Assert.Equal(origin, parsed.OriginAgent); Assert.Contains("kept", parsed.Tags);
        Assert.Contains("nested: preserved", parsed.Frontmatter);
        if (color is not null) { Assert.Contains("origin/" + origin, parsed.Tags); }
    }

    /// <summary>Checks creator immutability through edits, rename, move, rebuild and restart.</summary>
    [Fact]
    public async Task CreatorSurvivesEditsMovesAndCacheRebuild()
    {
        BrainService brain = Create();
        BrainNote created = await brain.SaveAsync(Note("Knowledge/fixture.md", "Original", "Synthetic body") with { OriginAgent = "codex", Tags = ["existing"] });
        BrainNote edited = await brain.SaveAsync(created with { OriginAgent = "claude", Tags = ["added"], Title = "Renamed" });
        Assert.Equal("codex", edited.OriginAgent); Assert.Contains("existing", edited.Tags); Assert.Contains("added", edited.Tags);
        await brain.MoveAsync(edited.Path, "Knowledge/Moved/renamed.md");
        await brain.RefreshAsync(true);
        BrainNote moved = brain.Snapshot.Notes.Single(note => note.Id == created.Id);
        Assert.Equal("codex", moved.OriginAgent); Assert.Equal("Renamed", moved.Title);
        BrainService restarted = Create(cache: "fresh-provenance-cache"); await restarted.InitializeAsync();
        Assert.Equal("codex", restarted.Snapshot.Notes.Single(note => note.Id == created.Id).OriginAgent);
    }

    /// <summary>Leaves old documents and imported skills neutral, even after a native edit.</summary>
    [Fact]
    public async Task LegacyAndImportedOriginsAreNotInvented()
    {
        BrainService brain = Create(); await brain.InitializeAsync();
        var storage = new FileBrainStorage(brain.Location);
        storage.Write("legacy.md", "---\nid: legacy\ntags: [kept]\ncustom: retained\n---\nSynthetic legacy", null);
        await brain.RefreshAsync(); BrainNote legacy = brain.Snapshot.Notes.Single(note => note.Id == "legacy");
        BrainNote saved = await brain.SaveAsync(legacy with { OriginAgent = "laura", Tags = [] });
        Assert.Empty(saved.OriginAgent); Assert.Contains("kept", saved.Tags); Assert.Contains("custom: retained", saved.Frontmatter);
        var skills = new BrainSkills(brain, _settings);
        BrainNote imported = await skills.ImportAsync("# Synthetic skill", "Synthetic");
        Assert.Empty(imported.OriginAgent); Assert.False(skills.IsEnabled(imported));
        BrainNote attributed = await skills.ImportAsync("---\norigin_agent: claude\ntags: [custom]\n---\nSynthetic", "Attributed");
        Assert.Equal("claude", attributed.OriginAgent); Assert.Contains("origin/claude", attributed.Tags); Assert.Contains("custom", attributed.Tags);
    }

    /// <summary>Reports conflicting tags while keeping metadata as the graph authority.</summary>
    [Fact]
    public async Task ContradictoryTagsAreReportedWithoutChangingCreator()
    {
        BrainService brain = Create();
        BrainNote saved = await brain.SaveAsync(Note("conflict.md", "Synthetic", "Synthetic") with { OriginAgent = "codex", Tags = ["origin/claude"] });
        Assert.Equal("#3B82F6", BrainProvenance.Color(saved.OriginAgent));
        Assert.Contains("origin/claude", saved.Tags); Assert.Contains("origin/codex", saved.Tags);
        Assert.Contains(brain.Snapshot.Issues, issue => issue.Path == saved.Path && issue.Code == "origin_tag_conflict");
    }

    /// <summary>Resolves configuration as an optional candidate without changing active storage.</summary>
    [Fact]
    public void SharedConfigurationNeverChangesTheSelectedLocation()
    {
        string config = Path.Combine(_root, "shared-config.json");
        Assert.Null(SharedBrainConfiguration.ReadCandidate(config));
        string candidate = Path.Combine(_root, "other-vault");
        File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(new { vaultPath = candidate }));
        Assert.Equal(candidate, SharedBrainConfiguration.ReadCandidate(config));
        Assert.Equal(_settings.Current.Brain.Directory, Create().Location);
        File.WriteAllText(config, "{\"vaultPath\":\"relative\"}");
        Assert.Throws<IOException>(() => SharedBrainConfiguration.ReadCandidate(config));
        File.WriteAllText(config, "{"); Assert.Throws<IOException>(() => SharedBrainConfiguration.ReadCandidate(config));
    }
    /// <summary>Preserves settings and unrelated groups when installing Obsidian colors in isolated storage.</summary>
    [Fact]
    public async Task ObsidianGroupsAreExplicitIdempotentAndPreserveSettings()
    {
        BrainService brain = Create(); await brain.InitializeAsync();
        var storage = new FileBrainStorage(brain.Location);
        const string path = ".obsidian/graph.json";
        Assert.ThrowsAny<IOException>(() => storage.Read(path));
        storage.Write(path, "{\"showTags\":false,\"custom\":{\"kept\":true},\"colorGroups\":[{\"query\":\"tag:#personal\",\"color\":{\"a\":1,\"rgb\":123}}]}", null);
        await brain.ConfigureObsidianGroupsAsync(); string first = storage.Read(path);
        await brain.ConfigureObsidianGroupsAsync(); Assert.Equal(first, storage.Read(path));
        using var document = System.Text.Json.JsonDocument.Parse(first);
        Assert.False(document.RootElement.GetProperty("showTags").GetBoolean());
        Assert.True(document.RootElement.GetProperty("custom").GetProperty("kept").GetBoolean());
        Assert.Equal(4, document.RootElement.GetProperty("colorGroups").GetArrayLength());
        foreach (string agent in new[] { "laura", "codex", "claude" })
        {
            var group = document.RootElement.GetProperty("colorGroups").EnumerateArray().Single(group => group.GetProperty("query").GetString() == "tag:#origin/" + agent);
            Assert.Equal(Convert.ToInt32(BrainProvenance.Color(agent)![1..], 16), group.GetProperty("color").GetProperty("rgb").GetInt32());
        }
        string conflicting = first.Replace("tag:#personal", "tag:#origin/codex", StringComparison.Ordinal);
        Assert.Throws<IOException>(() => BrainObsidianGroups.Merge(conflicting));
        Assert.Throws<IOException>(() => BrainObsidianGroups.Merge("{\"colorGroups\":[1]}"));
    }

    /// <summary>Retains long tag lists and scalar tag metadata without truncation.</summary>
    [Fact]
    public void ExistingTagsAreMergedLosslessly()
    {
        string[] tags = Enumerable.Range(0, 130).Select(index => "tag/" + index).ToArray();
        BrainNote parsed = BrainMarkdown.Parse("---\ntags: [" + string.Join(", ", tags) + "]\n---\nSynthetic", "fixture.md");
        BrainNote saved = BrainMarkdown.Parse(BrainMarkdown.Render(parsed with { OriginAgent = "claude", Tags = ["added"] }), parsed.Path);
        Assert.All(tags, tag => Assert.Contains(tag, saved.Tags));
        Assert.Contains("origin/claude", saved.Tags); Assert.Contains("added", saved.Tags);
        BrainNote scalar = BrainMarkdown.Parse("---\ntags: existing\n---\nSynthetic", "scalar.md");
        Assert.Contains("existing", BrainMarkdown.Parse(BrainMarkdown.Render(scalar), scalar.Path).Tags);
    }
}

using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task McpSkillRequiresActualConnectedToolAndDependencyWithoutLosingExactReviewOnDisconnect()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); bool connected = false;
        var skills = new BrainSkills(brain, _settings, () => connected ? new HashSet<string> { "mcp.fixture.echo" } : new HashSet<string>(), availableDependency: dependency => connected && dependency == "mcp:fixture");
        BrainSkillProposal proposal = Procedure(source, "Synthetic MCP procedure");
        BrainNote draft = await skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Kind = "mcp", Dependencies = ["mcp:fixture"], Tools = ["mcp.fixture.echo"] } });
        Assert.Equal("blocked", skills.Inspect(draft).State); await Assert.ThrowsAsync<InvalidOperationException>(() => skills.SetEnabledAsync(draft, true));
        connected = true; BrainNote approved = await skills.SetEnabledAsync(draft, true); Assert.True(skills.IsEnabled(approved));
        connected = false; Assert.True(skills.Inspect(approved).Approved); Assert.False(skills.IsEnabled(approved)); Assert.Contains("unavailable_tool:mcp.fixture.echo", skills.Inspect(approved).Missing);
        connected = true; Assert.True(skills.IsEnabled(approved)); Assert.Equal(Laura.Core.Permissions.ApprovalMode.Ask, _settings.Current.Permissions.Mode);
    }
    private static BrainSkillProposal Procedure(BrainNote source, string name = "Synthetic verification") => new(name, "Inspect observed process outcomes without claiming task success.",
        new("guidance", "Distinguish process observations from task success.", ["Reviewing an observed process result"], [], [], [], [],
            ["Read the observed exit code and identify remaining uncertainty."], ["An exit code does not prove the full task outcome."],
            ["Report the observed result separately from the unverified goal."], ["Keep missing observations explicit; do not fabricate evidence."],
            ["process observation review"], [new(source.Id, source.Hash, "An exit code does not prove the full task outcome.")], []));
    private async Task<BrainNote> ProcedureSourceAsync(BrainService brain) => await brain.SaveAsync(Note("Knowledge/ProcedureSource.md", "Synthetic process evidence", "An exit code does not prove the full task outcome."));
    [Fact]
    public async Task GroundedProcedureIsInertPortableAndActivatedOnlyByExactLocalReview()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        BrainNote draft = await skills.ProposeAsync(Procedure(source));
        Assert.Equal("draft", draft.Status); Assert.Equal("unverified", draft.Verification); Assert.False(skills.IsEnabled(draft)); Assert.Empty(_settings.Current.Brain.TrustedSkills);
        Assert.Equal(source.Id, Assert.Single(draft.Relations).Target); Assert.NotNull(draft.Procedure); Assert.Contains("Success criteria", draft.Body, StringComparison.Ordinal);
        BrainNote parsed = BrainMarkdown.Parse(await brain.ReadAsync(draft.Path), draft.Path);
        Assert.Contains("name: Synthetic verification", parsed.Frontmatter, StringComparison.Ordinal);
        Assert.Equal(draft.Procedure.Sources.Single(), parsed.Procedure!.Sources.Single()); Assert.Equal(draft.Procedure.Steps, parsed.Procedure.Steps);
        Assert.DoesNotContain(await brain.SearchAsync("process observation review"), hit => hit.Note.Id == draft.Id);
        draft = await skills.SetEnabledAsync(draft, true); Assert.True(skills.IsEnabled(draft)); Assert.True(skills.Inspect(draft).Ready);
        BrainService reloaded = Create(cache: "procedure-restart"); await reloaded.InitializeAsync(); var restored = new BrainSkills(reloaded, _settings);
        Assert.True(restored.IsEnabled(reloaded.Snapshot.Notes.Single(note => note.Id == draft.Id)));
        await brain.SaveAsync(source with { Body = source.Body + "\nSource changed." });
        Assert.False(skills.IsEnabled(draft)); Assert.Contains(skills.Inspect(draft).Missing, reason => reason.StartsWith("source_version:", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ForgedStaleAndOversizedProcedureEvidenceCannotCreateSkillFiles()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        BrainSkillProposal proposal = Procedure(source);
        var rejected = await Assert.ThrowsAsync<IOException>(() => skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Sources = [new(source.Id, source.Hash, "Invented evidence not present in source.")] } }));
        Assert.Equal(BrainSkillContract.CurrentLiteralSource, rejected.Data[nameof(BrainSkillContract)]);
        await brain.SaveAsync(source with { Body = source.Body + "\nChanged." }); await Assert.ThrowsAsync<IOException>(() => skills.ProposeAsync(proposal));
        await Assert.ThrowsAsync<ArgumentException>(() => skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Steps = [new string('x', 1001)] } }));
        await Assert.ThrowsAsync<ArgumentException>(() => skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Sources = [new(null!, source.Hash, "Literal evidence")] } }));
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Type == "skill");
    }
    [Fact]
    public async Task RevisionPreservesIdentityAndHistoryButInvalidatesPriorApprovalAndRejectsDuplicates()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        BrainSkillProposal proposal = Procedure(source); BrainNote original = await skills.ProposeAsync(proposal); original = await skills.SetEnabledAsync(original, true);
        await Assert.ThrowsAsync<IOException>(() => skills.ProposeAsync(proposal));
        BrainNote revised = await skills.ProposeAsync(proposal with { TargetId = original.Id, TargetHash = original.Hash,
            Definition = proposal.Definition with { Steps = ["Read actual exit status.", "Report that the goal remains unverified."] } });
        Assert.Equal(original.Id, revised.Id); Assert.False(skills.IsEnabled(revised)); Assert.Equal("draft", revised.Status);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body == original.Body);
        await Assert.ThrowsAsync<IOException>(() => skills.SetEnabledAsync(original, true));
        await Assert.ThrowsAsync<IOException>(() => skills.ProposeAsync(proposal with { TargetId = original.Id, TargetHash = original.Hash }));
    }
    [Fact]
    public async Task MissingDependenciesAndUnknownToolsStayBlockedWithoutInstallingOrGrantingAnything()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings, () => new HashSet<string> { "system_status" });
        BrainSkillProposal proposal = Procedure(source);
        BrainNote draft = await skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Kind = "mcp", Dependencies = ["REA"], Tools = ["mcp.rea.inspect"] } });
        Assert.Equal("blocked", skills.Inspect(draft).State); Assert.Contains("unverified_dependency:REA", skills.Inspect(draft).Missing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => skills.SetEnabledAsync(draft, true)); Assert.Empty(_settings.Current.Brain.TrustedSkills);
        BrainNote native = await skills.ProposeAsync(Procedure(source, "Synthetic native review") with { Definition = proposal.Definition with { Kind = "native", Tools = ["system_status"], Permissions = ["Per-operation native policy"] } });
        native = await skills.SetEnabledAsync(native, true); Assert.True(skills.IsEnabled(native)); Assert.Contains("available_instructions", skills.Catalogue(6000), StringComparison.Ordinal);
        Assert.Equal(Laura.Core.Permissions.ApprovalMode.Ask, _settings.Current.Permissions.Mode);
    }
    [Fact]
    public async Task CompositionRequiresExactApprovedComponentsAndStopsWhenAComponentChanges()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        BrainNote component = await skills.ProposeAsync(Procedure(source)); BrainSkillProposal parent = Procedure(source, "Synthetic composed review");
        BrainNote draft = await skills.ProposeAsync(parent with { Definition = parent.Definition with { Kind = "composition", Components = [new(component.Id, component.Hash)] } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => skills.SetEnabledAsync(draft, true));
        component = await skills.SetEnabledAsync(component, true);
        draft = await skills.ProposeAsync(parent with { TargetId = draft.Id, TargetHash = draft.Hash,
            Definition = parent.Definition with { Kind = "composition", Components = [new(component.Id, component.Hash)] } });
        draft = await skills.SetEnabledAsync(draft, true); Assert.True(skills.IsEnabled(draft));
        await skills.SetEnabledAsync(component, false); Assert.False(skills.IsEnabled(draft));
        await Assert.ThrowsAsync<IOException>(() => skills.ProposeAsync(parent with { TargetId = draft.Id, TargetHash = draft.Hash,
            Definition = parent.Definition with { Kind = "composition", Components = [new(draft.Id, draft.Hash)] } }));
    }
    [Fact]
    public async Task ConflictingComponentIdentitiesBlockDiscoveryWithoutThrowingOrGrantingApproval()
    {
        BrainService brain = Create(); BrainNote source = await ProcedureSourceAsync(brain); var skills = new BrainSkills(brain, _settings);
        BrainNote component = await skills.ProposeAsync(Procedure(source)); component = await skills.SetEnabledAsync(component, true);
        BrainSkillProposal proposal = Procedure(source, "Synthetic conflict review");
        BrainNote parent = await skills.ProposeAsync(proposal with { Definition = proposal.Definition with { Kind = "composition", Components = [new(component.Id, component.Hash)] } });
        parent = await skills.SetEnabledAsync(parent, true);
        string copiedPath = Path.Combine(brain.Location, "Skills", "Conflict", "SKILL.md"); Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
        await File.WriteAllTextAsync(copiedPath, await brain.ReadAsync(component.Path)); await brain.RefreshAsync();
        Assert.False(skills.IsEnabled(parent)); Assert.Equal("blocked", skills.Inspect(parent).State);
        Assert.Contains("component_review:", skills.Catalogue(10000), StringComparison.Ordinal);
    }
}

using System.Text;
using Laura.Core.Text;

namespace Laura.Core.Brain;

public sealed partial class BrainSkills
{
    /// <summary>Builds an inert, evidence-linked draft or revises one exact version with ordinary revision history. Never activates dependencies or executes steps.</summary>
    public async Task<BrainNote> ProposeAsync(BrainSkillProposal proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(proposal.Definition);
        ArgumentNullException.ThrowIfNull(proposal.TargetId); ArgumentNullException.ThrowIfNull(proposal.TargetHash);
        string vault = brain.Location;
        await brain.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        BrainSkillDefinition definition = proposal.Definition;
        ValidateDefinition(proposal.Name, proposal.Description, definition);
        foreach (BrainSkillReference source in definition.Sources)
        {
            BrainNote? actual = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && note.Id == source.Id && note.Hash == source.Hash);
            if (actual is null || brain.Snapshot.Issues.Any(issue => issue.Path == actual.Path && issue.Code == "conflicting_id")
                || source.Evidence.Length < 8 || !actual.Body.Contains(source.Evidence, StringComparison.Ordinal))
            { throw new IOException("Procedural evidence must cite an exact current knowledge version and literal source text."); }
        }
        foreach (BrainSkillReference component in definition.Components)
        {
            if (brain.Snapshot.Notes.Count(note => note.Type == "skill" && note.Id == component.Id && note.Hash == component.Hash && note.Status != "archived") != 1)
            { throw new IOException("A composed skill requires an actual current component version."); }
        }
        BrainNote? original = null;
        if (proposal.TargetId.Length > 0)
        {
            original = brain.Snapshot.Notes.SingleOrDefault(note => note.Type == "skill" && note.Id == proposal.TargetId && note.Hash == proposal.TargetHash && note.Status != "archived")
                ?? throw new IOException("The proposed revision does not match a current skill.");
            if (definition.Components.Any(component => component.Id == original.Id)) { throw new IOException("A skill cannot compose itself."); }
        }
        else if (brain.Snapshot.Notes.Any(note => note.Type == "skill" && note.Status != "archived" && TextNormalizer.Normalize(note.Title) == TextNormalizer.Normalize(proposal.Name)))
        { throw new IOException("An equivalent named skill exists. Reuse it or propose an exact-version revision."); }
        string key = original?.Key ?? "skill.proposal." + FileBrainStorage.Hash(TextNormalizer.Normalize(proposal.Name))[..20];
        var body = new StringBuilder("# " + proposal.Name + "\n\nProposed procedure; not a verified capability or execution authorization.\n");
        Section("Purpose", [definition.Purpose]); Section("Use when", definition.UseWhen); Section("Prerequisites", definition.Prerequisites);
        Section("Declared dependencies — availability not verified", definition.Dependencies); Section("Required tools", definition.Tools);
        Section("Required permissions — each effect remains guarded", definition.Permissions); Section("Procedure", definition.Steps);
        Section("Limitations", definition.Limitations); Section("Success criteria — not yet observed", definition.SuccessCriteria); Section("Failure behavior", definition.FailureBehavior);
        Section("Sources", definition.Sources.Select(source => "[[id:" + source.Id + "]] (version " + source.Hash + ")\n> " + source.Evidence.Replace("\n", "\n> ", StringComparison.Ordinal)).ToArray());
        Section("Components", definition.Components.Select(component => "[[id:" + component.Id + "]] (version " + component.Hash + ")").ToArray());
        BrainRelation[] relations = definition.Sources.Select(source => new BrainRelation(source.Id, "derived_from", .8, .5, source.Evidence, "skill_proposal"))
            .Concat(definition.Components.Select(component => new BrainRelation(component.Id, "uses", .8, .5, "Exact declared procedural component", "skill_proposal")))
            .Concat(original?.Relations.Where(relation => relation.Source != "skill_proposal") ?? []).ToArray();
        return await brain.SaveAtLocationAsync((original ?? new BrainNote { Path = "Skills/" + FileBrainStorage.Hash(key)[..16] + "/SKILL.md", Key = key })
            with { Title = proposal.Name, Description = proposal.Description, Type = "skill", MemoryType = "procedural", Status = "draft", Source = "grounded_skill_proposal",
                Verification = "unverified", Confidence = .5, Body = body.ToString(), Procedure = definition, Relations = relations }, vault, cancellationToken).ConfigureAwait(false);
        void Section(string heading, IReadOnlyList<string> items)
        { body.Append("\n## ").Append(heading).Append('\n'); foreach (string item in items) { body.Append("\n- ").Append(item).Append('\n'); } }
    }
    /// <summary>Reports conservative readiness from current metadata, exact references and registered native tools; unknown dependencies never count as installed.</summary>
    public BrainSkillAvailability Inspect(BrainNote note)
    {
        var missing = new List<string>(); Check(note, new HashSet<string>(StringComparer.Ordinal), 0);
        bool approved = Approved(note); return new(approved, approved && missing.Count == 0,
            missing.Count > 0 ? "blocked" : approved ? note.Procedure is null ? "instructions_only" : "available_instructions" : "review_required", missing.Distinct(StringComparer.Ordinal).Take(32).ToArray());
        void Check(BrainNote candidate, HashSet<string> visited, int depth)
        {
            if (depth > 4 || !visited.Add(candidate.Id)) { missing.Add("composition_cycle_or_depth"); return; }
            if (!Current(candidate) || candidate.Type != "skill" || candidate.Status == "archived") { missing.Add("current_skill_version"); return; }
            if (candidate.Procedure is not { } definition) { visited.Remove(candidate.Id); return; }
            try { ValidateDefinition(candidate.Title, candidate.Description, definition); }
            catch (ArgumentException) { missing.Add("invalid_procedure_metadata"); visited.Remove(candidate.Id); return; }
            foreach (string dependency in definition.Dependencies) { missing.Add("unverified_dependency:" + dependency); }
            foreach (string prerequisite in definition.Prerequisites) { missing.Add("unverified_prerequisite:" + prerequisite); }
            if (definition.Kind is "mcp" or "code") { missing.Add("external_runtime_not_verified"); }
            IReadOnlySet<string> registered = availableTools?.Invoke() ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (string tool in definition.Tools.Where(tool => !registered.Contains(tool))) { missing.Add("unavailable_tool:" + tool); }
            foreach (BrainSkillReference source in definition.Sources)
            {
                if (source.Evidence.Length < 8 || !brain.Snapshot.Notes.Any(actual => actual.IsKnowledge && actual.Id == source.Id && actual.Hash == source.Hash
                    && !brain.Snapshot.Issues.Any(issue => issue.Path == actual.Path && issue.Code == "conflicting_id") && actual.Body.Contains(source.Evidence, StringComparison.Ordinal)))
                { missing.Add("source_version:" + source.Id); }
            }
            foreach (BrainSkillReference component in definition.Components)
            {
                BrainNote? actual = brain.Snapshot.Notes.FirstOrDefault(item => item.Id == component.Id && item.Hash == component.Hash && item.Type == "skill");
                if (actual is null || !Approved(actual)) { missing.Add("component_review:" + component.Id); continue; }
                Check(actual, visited, depth + 1);
            }
            visited.Remove(candidate.Id);
        }
    }
    private bool Current(BrainNote note) => note.Hash.Length > 0 && brain.Snapshot.Notes.Count(actual => actual.Id == note.Id && actual.Hash == note.Hash && actual.Path == note.Path) == 1
        && !brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id");
    private static void ValidateDefinition(string name, string description, BrainSkillDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 240
            || definition.Kind is not ("guidance" or "native" or "mcp" or "code" or "composition") || string.IsNullOrWhiteSpace(definition.Purpose) || definition.Purpose.Length > 1000)
        { throw new ArgumentException("A procedure requires bounded identity, description, purpose and a supported kind."); }
        foreach (IReadOnlyList<string>? list in new[] { definition.UseWhen, definition.Prerequisites, definition.Dependencies, definition.Tools, definition.Permissions,
            definition.Steps, definition.Limitations, definition.SuccessCriteria, definition.FailureBehavior, definition.Capabilities })
        { if (list is null || list.Count > 16 || list.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 1000)) { throw new ArgumentException("Procedure fields exceed their bounds."); } }
        if (definition.UseWhen.Count == 0 || definition.Steps.Count == 0 || definition.Limitations.Count == 0 || definition.SuccessCriteria.Count == 0 || definition.FailureBehavior.Count == 0
            || definition.Capabilities.Count == 0 || definition.Sources is null || definition.Sources.Count is < 1 or > 8 || definition.Components is null || definition.Components.Count > 4
            || definition.Sources.Concat(definition.Components).Any(source => source is null || string.IsNullOrEmpty(source.Id) || source.Id.Length > 100
                || source.Hash is not { Length: 64 } || source.Evidence is null || source.Evidence.Length > 1000)
            || (definition.Kind == "guidance" && (definition.Tools.Count > 0 || definition.Dependencies.Count > 0 || definition.Components.Count > 0))
            || (definition.Kind == "composition" && definition.Components.Count == 0))
        { throw new ArgumentException("A procedure requires bounded evidence, steps, capabilities, limits and verifiable outcome criteria."); }
    }
}

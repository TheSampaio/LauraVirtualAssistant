using System.Diagnostics;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Mcp;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Validates real process-to-process recovery using metadata written only by an isolated diagnostic. Never replays a tool or resumes an approval.</summary>
internal static class BrainRestartSmokeRunner
{
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 16 };
    private sealed record Version(string Id, string Path, string Hash, string Status);
    private sealed record Skill(string Id, string Hash, bool Approved, bool Ready);
    private sealed record Query(string Text, string Id);
    private sealed record Manifest(int Version, int Process, string VaultIdentity, IReadOnlyList<Version> Notes, IReadOnlyList<Skill> Skills, IReadOnlyList<BrainSkillOutcome> Outcomes, IReadOnlyList<Query> Queries, int AuditCount);
    internal static async Task WriteAsync(IServiceProvider services, string root)
    {
        var brain = services.GetRequiredService<BrainService>(); var skills = services.GetRequiredService<BrainSkills>();
        await brain.RefreshAsync().ConfigureAwait(true);
        BrainNote[] selected = brain.Snapshot.Notes.Where(note => note.IsKnowledge || note.Type == "skill" && note.Status != "archived").Take(200).ToArray();
        BrainNote native = skills.Find("Synthetic native diagnostic") ?? throw new InvalidOperationException("Native procedure absent from restart fixture.");
        BrainNote project = selected.Single(note => note.Type == "project" && note.Title.Contains("Synthetic Comet", StringComparison.Ordinal));
        var queries = new List<Query> { new("Synthetic Comet", project.Id) };
        BrainNote? rea = selected.SingleOrDefault(note => note.Source == "external_source" && note.Title.EndsWith("src/mcpStartupPolicy.ts", StringComparison.Ordinal));
        if (rea is not null) { queries.Add(new("MCP startup initialized policy", rea.Id)); }
        var manifest = new Manifest(1, Environment.ProcessId, FileBrainStorage.Hash(brain.Location), selected.Select(note => new Version(note.Id, note.Path, note.Hash, note.Status)).ToArray(),
            selected.Where(note => note.Type == "skill").Select(note => { BrainSkillAvailability state = skills.Inspect(note); return new Skill(note.Id, note.Hash, state.Approved, state.Ready); }).ToArray(),
            services.GetRequiredService<BrainSkillUsage>().Read(native.Id), queries, services.GetRequiredService<PermissionManager>().History.Count);
        await File.WriteAllTextAsync(Path.Combine(root, "restart-manifest.json"), JsonSerializer.Serialize(manifest)).ConfigureAwait(true);
    }
    internal static async Task<IReadOnlyList<string>> VerifyAsync(IServiceProvider services, string root)
    {
        string path = Path.Combine(root, "restart-manifest.json"); if (new FileInfo(path).Length > 128000) { throw new IOException("Restart manifest exceeds its bound."); }
        Manifest expected = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(path).ConfigureAwait(true), Json) ?? throw new IOException("Invalid restart manifest.");
        if (expected.Version != 1 || expected.Process == Environment.ProcessId || expected.Notes.Count is 0 or > 200) { throw new InvalidOperationException("Restart requires an actual prior diagnostic process."); }
        try { using Process old = Process.GetProcessById(expected.Process); if (!old.HasExited) { throw new InvalidOperationException("The previous diagnostic must exit before restart verification."); } } catch (ArgumentException) { }
        var brain = services.GetRequiredService<BrainService>(); var skills = services.GetRequiredService<BrainSkills>(); var permissions = services.GetRequiredService<PermissionManager>();
        if (FileBrainStorage.Hash(brain.Location) != expected.VaultIdentity) { throw new IOException("Restart fixture vault changed."); }
        await brain.RefreshAsync(rebuild: true).ConfigureAwait(true);
        foreach (Version version in expected.Notes)
        {
            if (!brain.Snapshot.Notes.Any(note => note.Id == version.Id && note.Path == version.Path && note.Hash == version.Hash && note.Status == version.Status)) { throw new InvalidOperationException("Authored version did not survive actual process restart/index rebuild."); }
        }
        foreach (Skill version in expected.Skills)
        {
            BrainNote note = brain.Snapshot.Notes.Single(note => note.Id == version.Id && note.Hash == version.Hash);
            BrainSkillAvailability actual = skills.Inspect(note);
            if (actual.Approved != version.Approved || actual.Ready != version.Ready) { throw new InvalidOperationException("Restart changed reviewed instruction availability."); }
        }
        var usage = services.GetRequiredService<BrainSkillUsage>();
        foreach (BrainSkillOutcome expectedOutcome in expected.Outcomes)
        { if (!usage.Read(expectedOutcome.SkillId).Contains(expectedOutcome)) { throw new InvalidOperationException("Native versioned outcome did not survive process restart."); } }
        foreach (Query query in expected.Queries)
        { if (!(await brain.SearchAsync(query.Text, 20).ConfigureAwait(true)).Any(hit => hit.Note.Id == query.Id)) { throw new InvalidOperationException("Restarted retrieval did not recover selected actual knowledge."); } }
        if (permissions.PendingRequests.Count != 0 || permissions.Grants.Count != 0 || permissions.History.Count != expected.AuditCount || services.GetRequiredService<McpRuntime>().ToolNames.Count != 0
            || usage.Capture(services.GetRequiredService<IConversationMemory>().WorkingContext.TurnId) is not null)
        { throw new InvalidOperationException("Restart replayed authorization/execution or retained transient skill attribution."); }
        return ["A new native LAURA process verified that the original diagnostic process had exited, rebuilt the derived index and recovered " + expected.Notes.Count + " exact authored IDs/paths/hashes/statuses and " + expected.Skills.Count + " exact skill approval/readiness states.",
            "Retained native process outcomes survived restart with their original version/event identity. Selected synthetic/public REA knowledge was retrieved again; no pending approval, execution grant, MCP capability or transient skill-use binding was replayed, and no protected action was executed."];
    }
}

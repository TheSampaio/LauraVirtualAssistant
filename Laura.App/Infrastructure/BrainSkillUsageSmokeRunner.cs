using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Uses harmless SDK diagnostics in the isolated workspace to distinguish actual native skill observations from instruction loading and denied execution.</summary>
internal static class BrainSkillUsageSmokeRunner
{
    internal static async Task<string> RunAsync(IServiceProvider services, string workspace, BrainNote evidence)
    {
        var brain = services.GetRequiredService<BrainService>(); var tools = services.GetRequiredService<IAssistantTools>(); var memory = services.GetRequiredService<IConversationMemory>();
        var permissions = services.GetRequiredService<PermissionManager>(); var center = services.GetRequiredService<WindowsApprovalCenter>();
        var usage = services.GetRequiredService<BrainSkillUsage>(); var skills = services.GetRequiredService<BrainSkills>();
        BrainSkillDefinition definition = new("native", "Observe native process exit without asserting full task success.", ["Testing native process observation"], [], [], ["run_program"],
            ["Each command requires its own exact native permission."], ["Run the user-approved diagnostic and inspect its actual exit."], ["No independent proof of goal success."],
            ["Report only actual native process evidence."], ["A nonzero exit requires investigation before a proposed revision."], ["native process diagnostic"], [new(evidence.Id, evidence.Hash, "Observed exit code: 0")], []);
        BrainNote draft = await skills.ProposeAsync(new("Synthetic native diagnostic", "Review a native SDK diagnostic with version-specific outcomes.", definition)).ConfigureAwait(true);
        memory.SetWorkingContext("Test the reviewed synthetic native process procedure.");
        using var read = JsonDocument.Parse(JsonSerializer.Serialize(new { name = draft.Id }));
        Task<AssistantToolResult> load = tools.ExecuteAsync("read_brain_skill", read.RootElement, CancellationToken.None);
        await ApproveAsync(false).ConfigureAwait(true); AssistantToolResult loaded = await load.ConfigureAwait(true);
        string checkPath = Path.Combine(Path.GetDirectoryName(workspace)!, "skill-usage-checks.txt");
        await File.WriteAllTextAsync(checkPath, "Loaded instructions=" + loaded.Text.StartsWith("Approved skill text", StringComparison.Ordinal) + "; bound=" + (usage.Capture(memory.WorkingContext.TurnId) is not null) + "\n").ConfigureAwait(true);
        if (usage.Read(draft.Id).Count != 0) { throw new InvalidOperationException("Loading instructions counted as execution."); }
        foreach (string decision in new[] { "success", "deny", "failure" })
        {
            using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { executable = "dotnet", arguments = decision == "failure" ? "[\"--laura-synthetic-invalid-option\"]" : "[\"--version\"]", directory = workspace, timeout = 15 }));
            Task<AssistantToolResult> command = tools.ExecuteAsync("run_program", request.RootElement, CancellationToken.None);
            await ApproveAsync(true, decision == "deny").ConfigureAwait(true);
            AssistantToolResult result = await command.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
            await File.AppendAllTextAsync(checkPath, decision + ": exit=" + result.Observation?.ExitCode + "; retained=" + usage.Read(draft.Id).Count + "; ready=" + skills.IsEnabled(skills.Find(draft.Id)!) + "\n").ConfigureAwait(true);
            if (decision == "deny" ? result.Observation is not null : decision == "success" ? result.Observation?.ExitCode != 0 : result.Observation is null || result.Observation.ExitCode == 0)
            { throw new InvalidOperationException("SDK diagnostic did not produce the specified actual observation."); }
        }
        BrainNote approved = skills.Find(draft.Id)!; IReadOnlyList<BrainSkillOutcome> outcomes = usage.Read(draft.Id, approved.Hash);
        if (outcomes.Count != 2 || outcomes.Count(item => item.ExitCode == 0) != 1 || outcomes.Any(item => item.SkillHash != approved.Hash) || !skills.IsEnabled(approved))
        { throw new InvalidOperationException("Native observations were not attributed to the exact reviewed procedure."); }
        string metadata = (await tools.ExecuteAsync("read_brain_skill_usage", read.RootElement, CancellationToken.None).ConfigureAwait(true)).Text;
        if (!metadata.Contains("not_independently_verified", StringComparison.Ordinal) || metadata.Contains("--laura-synthetic", StringComparison.Ordinal)) { throw new InvalidOperationException("Usage inspection leaked command arguments or misrepresented goal verification."); }
        memory.SetWorkingContext("A new turn must not inherit the previous skill.");
        using var nextRequest = JsonDocument.Parse(JsonSerializer.Serialize(new { executable = "dotnet", arguments = "[\"--version\"]", directory = workspace, timeout = 15 }));
        Task<AssistantToolResult> next = tools.ExecuteAsync("run_program", nextRequest.RootElement, CancellationToken.None);
        await ApproveAsync(true).ConfigureAwait(true); await next.ConfigureAwait(true);
        if (usage.Read(draft.Id).Count != 2) { throw new InvalidOperationException("A later user turn inherited stale skill attribution."); }
        return "Actual reviewed native skill recorded one zero and one nonzero SDK process exit against its exact version; instruction loading, denied execution and a later user turn produced no usage receipts. Inspection retained no arguments and did not claim goal success or auto-revise the skill.";

        async Task ApproveAsync(bool critical, bool deny = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (permissions.PendingRequests.Count == 0) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
            if (permissions.PendingRequests.Count != 1 || permissions.PendingRequests[0].Risk != (critical ? PermissionRisk.Critical : PermissionRisk.High)) { throw new InvalidOperationException("Expected exact native review boundary."); }
            center.Show(); await Task.Delay(150).ConfigureAwait(true);
            if (deny) { center.InvokeForSmoke("deny"); } else if (critical) { center.ConfirmCriticalForSmoke(); } else { center.InvokeForSmoke("once"); }
        }
    }
}

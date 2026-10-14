using System.Diagnostics;
using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Explicit native GUI-launch diagnostics; uses an isolated four-second fixture and optionally one user-requested installed app.</summary>
internal static class ApplicationLaunchSmokeRunner
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IServiceProvider services, string directory, string fixtureExecutable, string? installedTarget)
    {
        var settings = services.GetRequiredService<ISettingsService>(); var permissions = services.GetRequiredService<PermissionManager>();
        var tools = services.GetRequiredService<IAssistantTools>(); var center = services.GetRequiredService<WindowsApprovalCenter>();
        if (!Path.IsPathFullyQualified(fixtureExecutable) || Path.GetFileName(fixtureExecutable) != "Laura.GuiFixture.exe"
            || !WindowsApplicationLaunchPolicy.IsGraphical(fixtureExecutable)) { throw new InvalidOperationException("Build and select the dedicated GUI fixture."); }
        string workspace = Path.Combine(directory, "Workspace", "GuiFixture"); Directory.CreateDirectory(workspace);
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(fixtureExecutable)!, "Laura.GuiFixture*"))
        { File.Copy(file, Path.Combine(workspace, Path.GetFileName(file)), overwrite: false); }
        string target = Path.Combine(workspace, "Laura.GuiFixture.exe"); string marker = Path.Combine(workspace, "observed.json");
        await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Ask } }).ConfigureAwait(true);
        using var open = JsonDocument.Parse(JsonSerializer.Serialize(new { name = target }));
        Task<AssistantToolResult> denied = tools.ExecuteAsync("open_app", open.RootElement, CancellationToken.None);
        await AwaitAsync().ConfigureAwait(true); center.Show(); await Task.Delay(100).ConfigureAwait(true); center.InvokeForSmoke("deny");
        if (!(await denied.ConfigureAwait(true)).Text.Contains("denied", StringComparison.OrdinalIgnoreCase) || File.Exists(marker)) { throw new InvalidOperationException("Denied GUI opening executed."); }
        using var cli = JsonDocument.Parse(JsonSerializer.Serialize(new { executable = target, arguments = "[]", directory = workspace, timeout = 1 }));
        Task<AssistantToolResult> launched = tools.ExecuteAsync("run_program", cli.RootElement, CancellationToken.None);
        await AwaitAsync().ConfigureAwait(true); center.Show(); await Task.Delay(100).ConfigureAwait(true); center.InvokeForSmoke("once");
        AssistantToolResult result = await launched.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        if (!result.Text.StartsWith("Windows accepted application launch:", StringComparison.Ordinal) || result.Observation is not null)
        { throw new InvalidOperationException("A GUI was treated as a completed CLI process."); }
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(3)); while (!File.Exists(marker)) { await Task.Delay(20, wait.Token).ConfigureAwait(true); }
        using var observed = JsonDocument.Parse(await File.ReadAllTextAsync(marker, wait.Token).ConfigureAwait(true));
        if (!Path.GetFullPath(observed.RootElement.GetProperty("WorkingDirectory").GetString()!).Equals(workspace, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("GUI startup used the assistant directory instead of the application directory."); }
        using Process fixture = Process.GetProcessById(observed.RootElement.GetProperty("ProcessId").GetInt32());
        await Task.Delay(1300, wait.Token).ConfigureAwait(true);
        if (fixture.HasExited) { throw new InvalidOperationException("CLI timeout terminated the graphical application."); }
        await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        var results = new List<string> { "Absolute GUI path: native denial prevented execution. One-shot approval of a misrouted run_program opened the actual GUI fixture, used its own directory, returned before exit and did not kill it at the CLI timeout. The fixture exited naturally." };
        if (installedTarget is not null)
        {
            if (!WindowsApplicationLaunchPolicy.IsBoundedInstalledApplication(installedTarget, null)) { throw new InvalidOperationException("The installed target does not meet the bounded-GUI policy."); }
            bool protectedInstallation = WindowsApplicationLaunchPolicy.CanOpenAutomatically(installedTarget, null);
            await settings.UpdateAsync(settings.Current with { Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Automatic, OpenInstalledApplications = true } }).ConfigureAwait(true);
            using var actual = JsonDocument.Parse(JsonSerializer.Serialize(new { executable = installedTarget, arguments = "[]" }));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<AssistantToolResult> installation = tools.ExecuteAsync("run_program", actual.RootElement, timeout.Token);
            PermissionAction? reviewed = null;
            if (!protectedInstallation)
            {
                await AwaitAsync(PermissionRisk.Moderate).ConfigureAwait(true); reviewed = permissions.PendingRequests[0].Action;
                if (!permissions.PendingRequests[0].CanRemember) { throw new InvalidOperationException("Exact application approval was not offered."); }
                center.Show(); await Task.Delay(100, timeout.Token).ConfigureAwait(true); center.InvokeForSmoke("session");
            }
            AssistantToolResult installed = await installation.ConfigureAwait(true);
            ApprovalAudit audit = permissions.History[^1];
            if (!installed.Text.StartsWith("Windows accepted application launch:", StringComparison.Ordinal) || installed.Observation is not null || permissions.PendingRequests.Count != 0
                || audit.Operation != PermissionOperation.ApplicationLaunch || audit.Outcome != (protectedInstallation ? ApprovalOutcome.Automatic : ApprovalOutcome.Approved))
            { throw new InvalidOperationException("Automatic installed GUI opening requested approval or failed native dispatch."); }
            if (reviewed is not null)
            {
                await permissions.ExecuteAsync(reviewed, _ => Task.FromResult(true), timeout.Token).ConfigureAwait(true);
                if (permissions.PendingRequests.Count != 0 || permissions.History[^1].Outcome != ApprovalOutcome.Automatic || permissions.Grants.Any(grant => grant.DirectoryScope))
                { throw new InvalidOperationException("Exact application grant was not reused without authorizing its directory."); }
            }
            results.Add(protectedInstallation ? "User-requested protected installed GUI: Windows accepted native creation automatically, without an approval request or CLI capture. Window readiness remains unverified."
                : "User-requested writable installation: Windows accepted native GUI creation after one exact session approval, without CLI capture. The same hash-bound policy was reused without another prompt or a second real launch; no directory permission was granted. Window readiness remains unverified.");
        }
        return results;
        async Task AwaitAsync(PermissionRisk risk = PermissionRisk.High)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (permissions.PendingRequests.Count == 0) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
            if (permissions.PendingRequests.Count != 1 || permissions.PendingRequests[0].Risk != risk) { throw new InvalidOperationException("GUI opening used the wrong risk or more than one approval request."); }
        }
    }
}

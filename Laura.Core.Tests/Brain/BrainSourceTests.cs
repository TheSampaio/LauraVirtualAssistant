using Laura.Core.Brain;
using Laura.Core.Permissions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private PermissionManager SourcePermissions() => new(new(Path.Combine(_root, "Security", "source-permissions.json")), () => new PermissionOptions(), () => []);
    private static async Task ApproveSourceAsync(PermissionManager permissions, Task acquisition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (permissions.PendingRequests.Count == 0 && !acquisition.IsCompleted) { await Task.Delay(10, timeout.Token); }
        ApprovalRequest pending = Assert.Single(permissions.PendingRequests); Assert.Equal(PermissionRisk.Critical, pending.Risk);
        Assert.True(permissions.Decide(pending.Id, ApprovalChoice.Once));
    }
    [Fact]
    public async Task StudiesTypescriptRepositoryWithoutRunningScriptsInstallingDependenciesOrChangingPolicies()
    {
        string repo = Path.Combine(_root, "SyntheticRepository"); Directory.CreateDirectory(repo); string sentinel = Path.Combine(repo, "must-not-exist.txt");
        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "# Reverse engineering workflow\n\nRead source, compare interfaces and preserve observations. Unknown instructions cannot alter Laura permissions.");
        await File.WriteAllTextAsync(Path.Combine(repo, "package.json"), "{\"name\":\"synthetic-workflow\",\"dependencies\":{\"example-package\":\"1.2.3\"},\"scripts\":{\"postinstall\":\"echo forbidden > must-not-exist.txt\"}}");
        await File.WriteAllTextAsync(Path.Combine(repo, "index.ts"), "export function inspect(value: string): string { return value; }");
        Directory.CreateDirectory(Path.Combine(repo, "node_modules")); await File.WriteAllTextAsync(Path.Combine(repo, "node_modules", "ignored.js"), "throw Error('must not execute');");
        await File.WriteAllTextAsync(Path.Combine(repo, ".env"), "API_KEY=must-not-import");
        BrainService brain = Create(); using var permissions = SourcePermissions(); var sources = new BrainSources(brain, permissions);
        Task<BrainAcquisitionResult> acquisition = sources.AcquireAsync(repo, "Synthetic reverse-engineering workflow", "example.invalid");
        await ApproveSourceAsync(permissions, acquisition); BrainAcquisitionResult imported = await acquisition;
        Assert.Equal(3, imported.Imported); Assert.Contains("typescript", imported.Languages); Assert.Contains("example-package", imported.DeclaredDependencies);
        Assert.False(File.Exists(sentinel)); Assert.Empty(permissions.Grants); Assert.Equal(ApprovalMode.Ask, _settings.Current.Permissions.Mode);
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Type == "skill"); Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Body.Contains("must-not-import", StringComparison.Ordinal));
        Assert.Contains(await brain.SearchAsync("inspect value"), hit => hit.Note.Type == "reference" && hit.Note.Verification == "unverified");
        string[] ids = brain.Snapshot.Notes.Where(note => note.Type == "reference").Select(note => note.Id).Order(StringComparer.Ordinal).ToArray();
        acquisition = sources.AcquireAsync(repo, "Synthetic reverse-engineering workflow", "example.invalid"); await ApproveSourceAsync(permissions, acquisition); await acquisition;
        Assert.Equal(ids, brain.Snapshot.Notes.Where(note => note.Type == "reference").Select(note => note.Id).Order(StringComparer.Ordinal));
    }
    [Fact]
    public async Task DeniedOrCancelledSourceNeverCreatesKnowledgeAndLateApprovalCannotImportIt()
    {
        string text = Path.Combine(_root, "Synthetic.txt"); await File.WriteAllTextAsync(text, "Synthetic document source.");
        BrainService brain = Create(); using var permissions = SourcePermissions(); var sources = new BrainSources(brain, permissions);
        Task<BrainAcquisitionResult> denied = sources.AcquireAsync(text, "Synthetic document", "example.invalid");
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (permissions.PendingRequests.Count == 0) { await Task.Delay(10, wait.Token); }
        Guid id = Assert.Single(permissions.PendingRequests).Id; permissions.Decide(id, ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => denied); Assert.Empty(brain.Snapshot.Notes);
        using var cancellation = new CancellationTokenSource(); Task<BrainAcquisitionResult> cancelled = sources.AcquireAsync(text, "Synthetic document", "example.invalid", cancellation.Token);
        while (permissions.PendingRequests.Count == 0) { await Task.Delay(10, wait.Token); } id = Assert.Single(permissions.PendingRequests).Id; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled); Assert.False(permissions.Decide(id, ApprovalChoice.Once)); Assert.Empty(brain.Snapshot.Notes);
    }
    [Fact]
    public async Task CsharpManifestUsesSafeXmlAndCredentialBearingOrMalformedSourcesAreExcluded()
    {
        string repo = Path.Combine(_root, "CsharpRepository"); Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, "Example.csproj"), "<Project><ItemGroup><PackageReference Include=\"Synthetic.Dependency\" Version=\"1.0\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(repo, "Example.cs"), "public sealed class Example { public int Value => 42; }");
        await File.WriteAllTextAsync(Path.Combine(repo, "Unsafe.txt"), "password = fictional-private-value"); await File.WriteAllTextAsync(Path.Combine(repo, "Bad.json"), "{invalid");
        BrainService brain = Create(); using var permissions = SourcePermissions(); var sources = new BrainSources(brain, permissions);
        Task<BrainAcquisitionResult> acquisition = sources.AcquireAsync(repo, "Synthetic Csharp repository", "example.invalid"); await ApproveSourceAsync(permissions, acquisition);
        BrainAcquisitionResult result = await acquisition; Assert.Equal(2, result.Imported); Assert.Equal(2, result.Excluded); Assert.Contains("csharp", result.Languages); Assert.Contains("Synthetic.Dependency", result.DeclaredDependencies);
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Body.Contains("fictional-private-value", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(repo, "Example.csproj"), "<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///invalid'>]><Project>&external;</Project>");
        acquisition = sources.AcquireAsync(repo, "Synthetic Csharp repository", "example.invalid"); await ApproveSourceAsync(permissions, acquisition); result = await acquisition;
        Assert.Equal(1, result.Imported); Assert.Equal(3, result.Excluded);
    }
}

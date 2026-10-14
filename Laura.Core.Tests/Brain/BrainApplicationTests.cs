using System.Security.Cryptography;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Permissions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    private async Task<ProcessLaunchResult> SyntheticLaunchAsync(string name)
    {
        string workspace = Path.Combine(_root, "Workspace"); Directory.CreateDirectory(workspace); string path = Path.Combine(workspace, name + ".exe");
        byte[] artifact = [77, 90, 1, 2, 3, 4, 5]; await File.WriteAllBytesAsync(path, artifact);
        // This is a contract fixture, not a real executable or proof of actual Windows process creation.
        return new(true, path, 12345, Convert.ToHexString(SHA256.HashData(artifact)));
    }
    [Fact]
    public async Task ObservedApplicationReferenceSurvivesRestartPreservesIdentityAndRejectsChangedBytes()
    {
        BrainService brain = Create(); ProcessLaunchResult launch = await SyntheticLaunchAsync("ExampleEditor");
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        BrainNote original = (await apps.RememberAsync("Example Editor", "Editor", launch, brain.Location))!; Assert.NotNull(original.Application);
        Assert.DoesNotContain(launch.Target, original.Body, StringComparison.Ordinal); Assert.Contains("no permission", original.Body, StringComparison.Ordinal);
        string revision = original.Revision; await apps.RememberAsync("Example Editor", "Editor", launch, brain.Location);
        Assert.Equal(revision, brain.Snapshot.Notes.Single(note => note.Id == original.Id).Revision);
        await apps.RememberAsync("Other nickname", "My Editor", launch, brain.Location); Assert.Single(brain.Snapshot.Notes, note => note.Type == "application" && note.IsKnowledge);
        BrainService restarted = Create(cache: "application-restart"); using var restored = new BrainApplications(restarted, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        BrainApplicationReference reference = (await restored.FindAsync("MY EDITOR"))!; Assert.Equal(launch.Target, reference.Target); Assert.Equal(launch.TargetHash, reference.Hash);
        await File.WriteAllBytesAsync(launch.Target, [77, 90, 9, 9, 9, 9, 9]); Assert.Null(await restored.FindAsync("Editor"));
    }
    [Fact]
    public async Task MereShellAcceptanceAndDisabledLearningCannotCreateAnObservedApplication()
    {
        BrainService brain = Create(); ProcessLaunchResult launch = await SyntheticLaunchAsync("AcceptedOnly");
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        Assert.Null(await apps.RememberAsync("Accepted Editor", "Editor", launch with { ProcessId = null }, brain.Location));
        await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = false } });
        Assert.Null(await apps.RememberAsync("Accepted Editor", "Editor", launch, brain.Location)); Assert.Empty(brain.Snapshot.Notes);
    }
    [Fact]
    public async Task MalformedOrNetworkReferencesRemainInertAndAmbiguousAliasesDoNotSelectAnExecutable()
    {
        BrainService brain = Create(); ProcessLaunchResult first = await SyntheticLaunchAsync("FirstEditor"), second = await SyntheticLaunchAsync("SecondEditor");
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        await apps.RememberAsync("First Editor", "Ambiguous", first, brain.Location); await apps.RememberAsync("Second Editor", "Ambiguous", second, brain.Location);
        Assert.Null(await apps.FindAsync("Ambiguous"));
        await brain.SaveAsync(Note("Knowledge/Applications/Network.md", "Network Editor", "An untrusted external hint.") with
            { Type = "application", Application = new("Network Editor", "\\\\example.invalid\\share\\Editor.exe", new string('A', 64)) });
        Assert.Null(await apps.FindAsync("Network Editor"));
        await brain.SaveAsync(Note("Knowledge/Applications/Invalid.md", "Invalid Editor", "An untrusted malformed hint.") with
            { Type = "application", Application = new("Invalid Editor", "../Editor.exe", new string('A', 64)) });
        Assert.Null(await apps.FindAsync("Invalid Editor"));
    }
    [Fact]
    public async Task ApplicationHintCannotReferenceProtectedStorageOrRedirectKnowledgeToAnotherVault()
    {
        BrainService brain = Create(); ProcessLaunchResult launch = await SyntheticLaunchAsync("ScopedEditor");
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        string original = brain.Location; await brain.ChangeLocationAsync(Path.Combine(_root, "OtherVault"), false, false);
        Assert.Null(await apps.TryRememberAsync("Scoped Editor", "Editor", launch, original)); Assert.Equal(1, apps.FailedObservations);
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Type == "application");
        string protectedPath = Path.Combine(_root, "Security", "Fake.exe"); Directory.CreateDirectory(Path.GetDirectoryName(protectedPath)!); await File.WriteAllBytesAsync(protectedPath, [77, 90]);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => apps.RememberAsync("Protected Editor", "Editor", launch with { Target = protectedPath }, brain.Location));
        Assert.Equal(Laura.Core.Permissions.ApprovalMode.Ask, _settings.Current.Permissions.Mode);
    }
}

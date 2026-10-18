using Laura.Core.Brain;
using Laura.Core.Permissions;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    /// <summary>A changed inventoried source remains pending instead of being silently classified as unsupported content.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedLocalInventoryDoesNotConsumeOrExcludePendingSources(bool changeBytes)
    {
        string root = Path.Combine(_root, "ChangedInventory"); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "A.cs"); await File.WriteAllTextAsync(path, "// original synthetic source");
        await File.WriteAllTextAsync(Path.Combine(root, "B.cs"), "// pending synthetic source");
        BrainService brain = Create(); using var permissions = SourcePermissions(); var remote = new RecordedRepositorySource(0);
        var store = new FileRepositoryStudyStore(Path.Combine(_root, "ChangedInventoryState"), new TestPrivateDataProtector());
        var studies = new RepositoryStudy(brain, new(brain, permissions, remote), permissions, store, remote); Guid taskId = Guid.NewGuid();
        var starting = studies.StartAsync(root, "Changed inventory", taskId, ""); await ApproveStudyAsync(permissions, starting); var initial = await starting;
        if (changeBytes) { await File.WriteAllTextAsync(path, "// materially changed synthetic source with another size"); }
        else { File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1)); }
        var stopped = await studies.ContinueAsync(initial.Id, taskId, "", initial.Cursor);
        Assert.Equal("partial_failure", stopped.State); Assert.Contains("integrity", stopped.Failure, StringComparison.Ordinal);
        Assert.Equal(2, stopped.PendingFiles); Assert.Equal(0, stopped.ExcludedFiles); Assert.Equal(0, stopped.AcquiredFiles);
        Assert.Equal(initial.Cursor, stopped.Cursor); Assert.Empty(stopped.Documents);
        var restarted = new RepositoryStudy(brain, new(brain, permissions, remote), permissions, store, remote);
        var recovered = await restarted.StatusAsync(initial.Id, taskId, "");
        Assert.Equal(2, recovered.PendingFiles); Assert.Equal(0, recovered.ExcludedFiles); Assert.Equal(initial.Cursor, recovered.Cursor);
    }
    /// <summary>Traverses more than 5,000 frozen GitHub files through separate nonrecursive subtree operations and a restarted coordinator.</summary>
    [Fact]
    public async Task GitHubSubtreeInventoryResumesPastTheLegacyRecursiveFileLimit()
    {
        using var transport = new PagedRepositoryTransport(); using var http = new HttpClient(transport); using var source = new GitHubBrainSource(http);
        BrainService brain = Create(); using var permissions = SourcePermissions(); var protector = new TestPrivateDataProtector();
        string cache = Path.Combine(_root, "SubtreeProgress"); var store = new FileRepositoryStudyStore(cache, protector);
        var studies = new RepositoryStudy(brain, new(brain, permissions, source), permissions, store, source); Guid taskId = Guid.NewGuid();
        var starting = studies.StartAsync("https://github.com/example/paged", "Synthetic paged repository", taskId, ""); await ApproveStudyAsync(permissions, starting); var progress = await starting;
        Assert.Equal(1, progress.PendingInventoryDirectories); Assert.True(progress.InventoryTruncated); Assert.Equal(0, progress.InventoryFiles); Assert.Equal("partial", progress.State);
        int iterations = 0;
        while (progress.PendingInventoryDirectories > 0)
        {
            var restarted = new RepositoryStudy(brain, new(brain, permissions, source), permissions, new FileRepositoryStudyStore(cache, protector), source);
            int requestsBefore = transport.Requests.Count;
            progress = await restarted.ContinueAsync(progress.Id, taskId, "", progress.Cursor);
            Assert.InRange(transport.Requests.Count - requestsBefore, 1, 8); Assert.Empty(permissions.PendingRequests);
            Assert.Equal(PagedRepositoryTransport.Commit, progress.Revision); Assert.True(++iterations <= 4);
        }
        Assert.Equal(6000, progress.InventoryFiles); Assert.Equal(6000, progress.PendingFiles); Assert.Equal(0, progress.AcquiredFiles);
        Assert.False(progress.InventoryTruncated); Assert.Equal("partial", progress.State); Assert.Equal(23, transport.Requests.Count);
        Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Type == "reference");
    }

    /// <summary>Approves only the explicit repository study review, preserving higher cloud-disclosure risk.</summary>
    private static async Task ApproveStudyAsync(PermissionManager permissions, Task study)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (permissions.PendingRequests.Count == 0 && !study.IsCompleted) { await Task.Delay(10, timeout.Token); }
        var review = Assert.Single(permissions.PendingRequests); Assert.Equal(PermissionOperation.RepositoryStudyAuthorization, review.Action.Operation);
        Assert.Equal(review.Action.Destination.Length > 0 ? PermissionRisk.Critical : PermissionRisk.High, review.Risk);
        Assert.True(permissions.Decide(review.Id, ApprovalChoice.Once));
    }
    /// <summary>Inventories 5,000 local sources and resumes across process/service restarts without a 64-file inventory failure.</summary>
    [Fact]
    public async Task LargeLocalRepositoryPersistsCoverageAndResumesSeparateBoundedBatches()
    {
        string root = Path.Combine(_root, "LargeProject"); Directory.CreateDirectory(Path.Combine(root, "Module"));
        for (int file = 0; file < 5000; file++) { await File.WriteAllTextAsync(Path.Combine(root, "Module", "Source" + file.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".cs"), "// Synthetic source data; never execute.\n"); }
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Synthetic Lion Engine\nA repository study fixture.");
        await File.WriteAllTextAsync(Path.Combine(root, "Module", "Module.csproj"), "<Project><ItemGroup><PackageReference Include=\"Example.Package\" Version=\"1\" /><ProjectReference Include=\"../Common/Common.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(root, ".env"), "must-not-import");
        BrainService brain = Create(); using var permissions = SourcePermissions(); var source = new RecordedRepositorySource(0); var protector = new TestPrivateDataProtector();
        string cache = Path.Combine(_root, "StudyProgress"); var store = new FileRepositoryStudyStore(cache, protector);
        var studies = new RepositoryStudy(brain, new(brain, permissions, source), permissions, store, source); Guid taskId = Guid.NewGuid();
        Task<RepositoryStudyProgress> starting = studies.StartAsync(root, "Synthetic Lion Engine", taskId, ""); await ApproveStudyAsync(permissions, starting); var progress = await starting;
        Assert.Equal(5002, progress.InventoryFiles); Assert.Equal(5002, progress.PendingFiles); Assert.False(progress.InventoryTruncated); Assert.Equal("partial", progress.State);
        progress = await studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor);
        Assert.Equal(64, progress.AcquiredFiles); Assert.Equal(4938, progress.PendingFiles); Assert.Equal(64, progress.Documents.Count);
        Assert.Equal("README.md", progress.Documents[0].Path); Assert.Contains("Example.Package", progress.DeclaredDependencies); Assert.Contains("project:../Common/Common.csproj", progress.DeclaredDependencies);
        Assert.Empty(permissions.PendingRequests); Assert.DoesNotContain(brain.Snapshot.Notes, note => note.Body.Contains("must-not-import", StringComparison.Ordinal));
        var restarted = new RepositoryStudy(brain, new(brain, permissions, source), permissions, new FileRepositoryStudyStore(cache, protector), source);
        var second = await restarted.ContinueAsync(progress.Id, taskId, "", progress.Cursor);
        Assert.Equal(128, second.AcquiredFiles); Assert.Equal(4874, second.PendingFiles);
        Assert.Empty(progress.Documents.Select(file => file.Id).Intersect(second.Documents.Select(file => file.Id)));
        Assert.Equal(128, brain.Snapshot.Notes.Count(note => note.Type == "reference")); Assert.Empty(permissions.PendingRequests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ContinueAsync(progress.Id, taskId, "", progress.Cursor));
        using var afterRestartPermissions = SourcePermissions();
        var afterRestart = new RepositoryStudy(brain, new(brain, afterRestartPermissions, source), afterRestartPermissions, store, source);
        Task<RepositoryStudyProgress> status = afterRestart.StatusAsync(progress.Id, taskId, "generativelanguage.googleapis.com");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (afterRestartPermissions.PendingRequests.Count == 0) { await Task.Delay(10, deadline.Token); }
        var review = Assert.Single(afterRestartPermissions.PendingRequests); Assert.Equal("generativelanguage.googleapis.com", review.Action.Destination); Assert.Equal(taskId, review.Action.TaskId);
        afterRestartPermissions.Decide(review.Id, ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => status);
    }

    /// <summary>Re-verifies actual bytes across local versions while preserving stable notes and removed-file evidence.</summary>
    [Fact]
    public async Task RefreshDetectsChangedBytesPreservesIdentitiesAndDoesNotDeleteRemovedReferences()
    {
        string root = Path.Combine(_root, "VersionedProject"); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Source.cs"), removed = Path.Combine(root, "Removed.cs");
        await File.WriteAllTextAsync(path, "// original source version"); await File.WriteAllTextAsync(removed, "// removed source version");
        var remote = new RecordedRepositorySource(0); BrainService brain = Create(); using var permissions = SourcePermissions();
        var studies = new RepositoryStudy(brain, new(brain, permissions, remote), permissions, new FileRepositoryStudyStore(Path.Combine(_root, "Studies"), new TestPrivateDataProtector()), remote);
        Guid taskId = Guid.NewGuid(); var starting = studies.StartAsync(root, "Versioned project", taskId, ""); await ApproveStudyAsync(permissions, starting); var progress = await starting;
        progress = await studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor); var first = progress.Documents.Single(file => file.Path == "Source.cs"); var priorRemoved = progress.Documents.Single(file => file.Path == "Removed.cs");
        DateTime timestamp = File.GetLastWriteTimeUtc(path); await File.WriteAllTextAsync(path, "// modified source version"); File.SetLastWriteTimeUtc(path, timestamp); File.Delete(removed);
        await File.WriteAllTextAsync(Path.Combine(root, "Added.cs"), "// added source version");
        progress = await studies.StartAsync(root, "Versioned project", taskId, "", true); Assert.Equal(2, progress.PendingFiles); Assert.Equal(1, progress.RemovedFiles);
        progress = await studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor); var changed = progress.Documents.Single(file => file.Path == "Source.cs");
        Assert.Equal(first.Id, changed.Id); Assert.NotEqual(first.ByteHash, changed.ByteHash); Assert.Equal(1, progress.ChangedFiles);
        Assert.Equal("acquisition_complete", progress.State); Assert.Contains(brain.Snapshot.Notes, note => note.Id == priorRemoved.Id);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body.Contains("original source version", StringComparison.Ordinal));
    }

    /// <summary>Enforces remote per-operation file/byte budgets and honest truncated-inventory reporting.</summary>
    [Fact]
    public async Task RemoteStudyUsesFrozenRevisionAndSeparateRequestAndByteBudgetsPerBatch()
    {
        var source = new RecordedRepositorySource(60); BrainService brain = Create(); using var permissions = SourcePermissions();
        var studies = new RepositoryStudy(brain, new(brain, permissions, source), permissions, new FileRepositoryStudyStore(Path.Combine(_root, "RemoteStudies"), new TestPrivateDataProtector()), source);
        Guid taskId = Guid.NewGuid(); var starting = studies.StartAsync("https://github.com/example/synthetic", "Public synthetic repository", taskId, "generativelanguage.googleapis.com"); await ApproveStudyAsync(permissions, starting); var progress = await starting;
        Assert.Equal(60, progress.InventoryFiles); Assert.Equal(new string('b', 40), progress.Revision);
        foreach (int expected in new[] { 24, 48, 60 })
        { progress = await studies.ContinueAsync(progress.Id, taskId, "generativelanguage.googleapis.com", progress.Cursor); Assert.Equal(expected, progress.AcquiredFiles); Assert.InRange(progress.Documents.Count, 1, 24); Assert.Empty(permissions.PendingRequests); }
        Assert.Equal(60, source.Reads); Assert.Equal(1, source.Inspections); Assert.Equal("acquisition_complete", progress.State);
        var large = new RecordedRepositorySource(10, 65536) { Truncated = true };
        var bounded = new RepositoryStudy(brain, new(brain, permissions, large), permissions, new FileRepositoryStudyStore(Path.Combine(_root, "LargeRemoteStudies"), new TestPrivateDataProtector()), large);
        starting = bounded.StartAsync("https://github.com/example/large", "Large synthetic blobs", taskId, ""); await ApproveStudyAsync(permissions, starting); progress = await starting;
        progress = await bounded.ContinueAsync(progress.Id, taskId, "", progress.Cursor); Assert.Equal(4, progress.AcquiredFiles); Assert.Equal(4, large.Reads); Assert.True(progress.InventoryTruncated); Assert.Equal("partial", progress.State);
    }

    /// <summary>Preserves partial progress on cancellation and excludes binary/secret-bearing files without blocking all later sources.</summary>
    [Fact]
    public async Task CancellationAndSensitiveContentPreserveHonestRecoverableCoverage()
    {
        string root = Path.Combine(_root, "MixedProject"); Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "A.txt"), "api_key = fictional-private-value"); await File.WriteAllBytesAsync(Path.Combine(root, "B.txt"), [1, 0, 2]);
        await File.WriteAllTextAsync(Path.Combine(root, "C.cs"), "// allowed synthetic source");
        var source = new RecordedRepositorySource(10) { CancelOnRead = 4 }; BrainService brain = Create(); using var permissions = SourcePermissions();
        var store = new FileRepositoryStudyStore(Path.Combine(_root, "MixedStudies"), new TestPrivateDataProtector()); var studies = new RepositoryStudy(brain, new(brain, permissions, source), permissions, store, source);
        Guid taskId = Guid.NewGuid(); var starting = studies.StartAsync(root, "Mixed source", taskId, ""); await ApproveStudyAsync(permissions, starting); var progress = await starting;
        progress = await studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor); Assert.Equal(2, progress.ExcludedFiles); Assert.Equal(1, progress.AcquiredFiles); Assert.Equal(0, progress.PendingFiles);
        starting = studies.StartAsync("https://github.com/example/cancel", "Interrupted public source", taskId, ""); await ApproveStudyAsync(permissions, starting); progress = await starting;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor, cancellation.Token));
        progress = await studies.StatusAsync(progress.Id, taskId, ""); Assert.Equal(3, progress.AcquiredFiles); Assert.Equal(7, progress.PendingFiles); Assert.Equal("partial", progress.State);
        source.CancelOnRead = int.MaxValue; progress = await studies.ContinueAsync(progress.Id, taskId, "", progress.Cursor); Assert.Equal(10, progress.AcquiredFiles);
    }
}

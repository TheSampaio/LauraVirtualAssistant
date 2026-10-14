using System.Text.RegularExpressions;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed class BrainVisualTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "laura-visual-brain-" + Guid.NewGuid().ToString("N"));
    private BrainService _brain = null!;
    private static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jMioAAAAASUVORK5CYII=");
    private sealed class NoEmbedding : IBrainEmbedding { public string ModelId => ""; public float[] Embed(string text) => []; }
    public async Task InitializeAsync()
    {
        var settings = new StubSettingsService(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(_root, "Brain"), AutoLearn = false } });
        _brain = new(settings, new(_root), new(Path.Combine(_root, "Cache"), new NoEmbedding()), location => new FileBrainStorage(location), NullLogger<BrainService>.Instance);
        await _brain.InitializeAsync();
    }
    private static string Asset(BrainNote note) => BrainService.ResolveAttachment(note.Path, Regex.Match(note.Body, @"!\[[^\]]*\]\(([^)]+)\)").Groups[1].Value);
    [Fact]
    public async Task ChangedVaultCannotReuseAVisualAttachmentApprovalForAnotherDestination()
    {
        string approvedVault = _brain.Location;
        await _brain.ChangeLocationAsync(Path.Combine(_root, "AnotherBrain"), false, false);
        await Assert.ThrowsAsync<IOException>(() => _brain.AttachVisualAsync(Png, "Must not switch destination", expectedLocation: approvedVault));
        Assert.DoesNotContain(await _brain.FilesAsync(), path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_brain.Snapshot.Notes, note => note.Title == "Must not switch destination");
    }
    [Fact]
    public async Task ExistingNodeKeepsIdentityMetadataAndConnectionsAndImageIsRecoverable()
    {
        var related = await _brain.SaveAsync(new() { Path = "Projects/related.md", Title = "Related project", Body = "# Related" });
        var original = await _brain.SaveAsync(new() { Path = "Projects/engine.md", Title = "Lion Engine", Body = "# Lion Engine\nExisting observations.", Aliases = ["Lion"], Relations = [new(related.Id, "related_to", 1, 1, "Explicit shared development reference")] });
        var saved = await _brain.AttachVisualAsync(Png, "Lion Engine", original.Id);
        Assert.Equal(original.Id, saved.Id); Assert.Equal(original.Relations, saved.Relations); Assert.Equal(original.Aliases, saved.Aliases); Assert.Contains("Existing observations", saved.Body);
        Assert.Equal(Png, await _brain.ReadAttachmentAsync(Asset(saved))); Assert.StartsWith("Assets/Screenshots/", Asset(saved));
        Assert.Contains("Lion Engine", await _brain.RecallAsync("Lion Engine")); Assert.Contains(".png", await _brain.ReadAsync(saved.Path));
    }
    [Fact]
    public async Task NewTopicCreatesOneNodeAndDeduplicatesImageAcrossNotesAndRepeatedRequests()
    {
        var first = await _brain.AttachVisualAsync(Png, "Synthetic visual topic"); var again = await _brain.AttachVisualAsync(Png, "Synthetic visual topic");
        Assert.Equal(first.Id, again.Id); Assert.Equal(first.Hash, again.Hash);
        var second = await _brain.AttachVisualAsync(Png, "Another visual topic"); Assert.NotEqual(first.Id, second.Id); Assert.Equal(Asset(first), Asset(second));
        Assert.Single(await _brain.FilesAsync(), path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.Single(Regex.Matches(again.Body, "!\\[").Cast<Match>()); Assert.Empty(second.Relations);
    }
    [Fact]
    public async Task InvalidImagesAndUnknownTargetNodesNeverCreateReferences()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _brain.AttachVisualAsync([1, 2, 3], "bad image"));
        await Assert.ThrowsAsync<IOException>(() => _brain.AttachVisualAsync(Png, "unknown", "missing-id"));
        Assert.DoesNotContain(_brain.Snapshot.Notes, note => note.Title is "bad image" or "unknown");
    }
    [Theory]
    [InlineData("Projects/note.md", "../Assets/Screenshots/image.png", "Assets/Screenshots/image.png")]
    [InlineData("Projects/Deep/note.md", "../../Assets/image.png", "Assets/image.png")]
    [InlineData("note.md", "./Assets/image.png", "Assets/image.png")]
    public void RelativePathsResolveUnderThePortableVault(string owner, string reference, string expected) => Assert.Equal(expected, BrainService.ResolveAttachment(owner, reference));
    [Theory]
    [InlineData("../../outside.png")] [InlineData("https://example.invalid/image.png")] [InlineData("C:/private/image.png")] [InlineData("/absolute/image.png")]
    public void ExternalAndEscapingReferencesAreRejected(string reference) => Assert.Throws<IOException>(() => BrainService.ResolveAttachment("note.md", reference));
    [Fact]
    public async Task MovingCopyingAndArchivingNotesPreserveRecoverableImageReferences()
    {
        var note = await _brain.AttachVisualAsync(Png, "Moving visual reference"); string asset = Asset(note);
        await _brain.MoveAsync(note.Path, "Projects/Deep/moved.md"); var moved = _brain.Snapshot.Notes.Single(item => item.Id == note.Id);
        Assert.Equal(asset, Asset(moved)); Assert.Equal(Png, await _brain.ReadAttachmentAsync(Asset(moved)));
        var duplicate = await _brain.DuplicateAsync(moved, "References/copy.md"); Assert.Equal(asset, Asset(duplicate)); Assert.NotEqual(moved.Id, duplicate.Id);
        await _brain.DeleteAsync(moved.Path); var archived = _brain.Snapshot.Notes.Single(item => item.Id == note.Id); Assert.Equal(asset, Asset(archived));
        await Assert.ThrowsAsync<IOException>(() => _brain.DeleteAsync(asset)); Assert.Equal(Png, await _brain.ReadAttachmentAsync(asset));
    }
    [Fact]
    public async Task MissingAndCorruptedAssetsAreObservableRatherThanInvented()
    {
        var note = await _brain.AttachVisualAsync(Png, "Reference"); string asset = Asset(note); Assert.Equal(Png, await _brain.ReadAttachmentAsync(asset));
        File.Delete(Path.Combine(_brain.Location, asset)); await Assert.ThrowsAsync<FileNotFoundException>(() => _brain.ReadAttachmentAsync(asset));
    }
    public async Task DisposeAsync() { await _brain.DisposeAsync(); if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }
}

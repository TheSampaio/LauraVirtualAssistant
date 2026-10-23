using System.Collections.Immutable;
using Laura.Core.Screenshots;
using Laura.Core.Permissions;
using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Screenshots;

public sealed class ScreenshotDocumentTests
{
    [Fact]
    public void OriginalPrintScreenDefaultsMigrateTogetherWithoutChangingCustomChords()
    {
        var defaults = new ScreenshotOptions();
        var legacy = new ScreenshotOptions { RegionShortcut = "PrtSc", WindowShortcut = "Ctrl+PrtSc", FullScreenShortcut = "Shift+PrtSc", AllDisplays = false };
        var migrated = legacy.Sanitized(); Assert.Equal(defaults.RegionShortcut, migrated.RegionShortcut); Assert.Equal(defaults.WindowShortcut, migrated.WindowShortcut); Assert.Equal(defaults.FullScreenShortcut, migrated.FullScreenShortcut); Assert.False(migrated.AllDisplays);
        var custom = legacy with { RegionShortcut = "Ctrl+Alt+P" }; Assert.Equal(custom, custom.Sanitized());
        foreach (string chord in new[] { defaults.RegionShortcut, defaults.WindowShortcut, defaults.FullScreenShortcut })
        { Assert.True(ScreenshotOptions.TryParse(chord, out uint modifiers, out uint key)); Assert.NotEqual(0x2cu, key); Assert.Equal(0x4006u, modifiers); }
        Assert.False(ScreenshotOptions.TryParse("F12", out _, out _));
    }
    private static ScreenshotFrame Frame(int x = -100, int y = -50) => new(new(x, y, 10, 8), Enumerable.Range(0, 320).Select(index => (byte)(index % 256)).ToArray());
    private static ScreenshotAnnotation Annotation(ScreenshotTool tool = ScreenshotTool.Arrow) => new(Guid.NewGuid(), tool, [new(1, 1), new(8, 6)], 0xffff0000, 3);
    [Fact]
    public void PhysicalCropHandlesNegativeMonitorOriginsAndClampsOutsideTheFrame()
    {
        using var frame = Frame(); byte[] original = frame.Pixels.ToArray(); using var crop = frame.Crop(new(-98, -48, 3, 2));
        Assert.Equal(new ScreenshotBounds(-98, -48, 3, 2), crop.Bounds); Assert.Equal(original.AsSpan((2 * 10 + 2) * 4, 12).ToArray(), crop.Pixels.Span[..12].ToArray());
        using var clipped = frame.Crop(new(-105, -55, 8, 8)); Assert.Equal(new ScreenshotBounds(-100, -50, 3, 3), clipped.Bounds); Assert.Equal(original, frame.Pixels.ToArray());
    }
    [Fact]
    public void ReverseDirectionSelectionAndOffscreenRegionsAreNormalized()
    {
        Assert.Equal(new ScreenshotBounds(-30, -20, 50, 60), ScreenshotBounds.Between(new(20, 40), new(-30, -20)));
        using var frame = Frame(); Assert.Throws<ArgumentException>(() => frame.Crop(new(100, 100, 10, 10)));
    }
    [Theory]
    [InlineData(ScreenshotTool.Rectangle)] [InlineData(ScreenshotTool.Ellipse)] [InlineData(ScreenshotTool.Line)] [InlineData(ScreenshotTool.Arrow)]
    [InlineData(ScreenshotTool.Pen)] [InlineData(ScreenshotTool.Text)] [InlineData(ScreenshotTool.Highlight)]
    public void AnnotationHistoryNeverChangesOriginalPixelsAndSupportsRemovalAndRedo(ScreenshotTool tool)
    {
        using var document = new ScreenshotDocument(Frame()); byte[] original = document.Frame.Pixels.ToArray(); var annotation = Annotation(tool);
        document.Add(annotation); document.Update(annotation.Move(3, 2)); Assert.Equal(annotation.Id, Assert.Single(document.Annotations).Id);
        Assert.True(document.Undo()); Assert.Equal(annotation, Assert.Single(document.Annotations)); Assert.True(document.Redo());
        Assert.True(document.Remove(annotation.Id)); Assert.Empty(document.Annotations); Assert.True(document.Undo()); Assert.Single(document.Annotations);
        Assert.Equal(original, document.Frame.Pixels.ToArray());
    }
    [Fact]
    public void NewEditAfterUndoInvalidatesRedoAndHistoryIsBounded()
    {
        using var document = new ScreenshotDocument(Frame()); for (int index = 0; index < 120; index++) { document.Add(Annotation()); }
        int undone = 0; while (document.Undo()) { undone++; } Assert.Equal(100, undone); document.Add(Annotation()); Assert.False(document.CanRedo);
    }
    [Fact]
    public void HitTestingSelectsTopmostIdentityAndResizingPreservesStyles()
    {
        using var document = new ScreenshotDocument(Frame()); var first = Annotation(ScreenshotTool.Rectangle); var second = Annotation(ScreenshotTool.Ellipse); document.Add(first); document.Add(second);
        Assert.Equal(second.Id, document.Hit(new(4, 4))!.Id); var resized = second.Resize(14, 10); Assert.Equal(second.Id, resized.Id); Assert.Equal(second.Color, resized.Color); Assert.Equal(14, resized.Box().Width);
    }
    [Fact]
    public void InvalidOrOversizedImagesAndAnnotationsFailBeforeAllocationOrCommit()
    {
        Assert.Throws<ArgumentException>(() => new ScreenshotFrame(new(0, 0, 10000, 10000), [])); using var document = new ScreenshotDocument(Frame());
        Assert.Throws<ArgumentException>(() => document.Add(Annotation() with { Thickness = double.NaN }));
        Assert.Throws<ArgumentException>(() => document.Add(Annotation() with { Points = [new(double.PositiveInfinity, 2)] })); Assert.Empty(document.Annotations);
        document.Dispose(); Assert.Throws<ObjectDisposedException>(() => document.Frame.Pixels.ToArray());
    }
    [Theory]
    [InlineData("PrtSc", true)] [InlineData("Ctrl+PrtSc", true)] [InlineData("Shift+PrtSc", true)] [InlineData("Ctrl+Alt+P", true)]
    [InlineData("F12", false)] [InlineData("P", false)] [InlineData("Win+P", false)] [InlineData("Ctrl+Ctrl+P", false)] [InlineData("F13", false)]
    public void ShortcutParserRejectsAmbiguousAndReservedChords(string chord, bool valid) => Assert.Equal(valid, ScreenshotOptions.TryParse(chord, out _, out _));
    [Fact]
    public async Task NativeGestureNeverAuthorizesCloudTransmissionOrProcessExecution()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-gesture-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = new PermissionManager(new(Path.Combine(root, "permissions.json")), () => new()); int sent = 0;
            var task = manager.ExecuteAsync(new PermissionAction(PermissionOperation.ImageCloud, "native image button", "", parameters: "exact-image-hash", destination: "example.invalid", userInitiated: true), _ => Task.FromResult(++sent));
            Assert.Equal(PermissionRisk.Critical, Assert.Single(manager.PendingRequests).Risk); Assert.False(manager.Decide(manager.PendingRequests.Single().Id, ApprovalChoice.Persistent)); manager.Decide(manager.PendingRequests.Single().Id, ApprovalChoice.Deny);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => task); Assert.Equal(0, sent);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
    [Fact]
    public void RemoteFilesystemIsRejectedBeforeResourceMetadataAccess()
    {
        const string path = "\\\\server.invalid\\private\\image.png";
        Assert.True(PermissionPolicy.IsNetworkPath(path));
        Assert.Throws<PermissionDeniedException>(() => PermissionPolicy.ValidateResources(new(PermissionOperation.ScreenshotExport, "test", "", [path]), []));
        Assert.Equal(PermissionRisk.Critical, PermissionPolicy.Risk(new(PermissionOperation.BrainAttachment, "native", "", destination: path, userInitiated: true)));
    }
    [Fact]
    public async Task ScreenshotSettingsPersistAndInvalidShortcutFailsClosed()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-shortcuts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new IniSettingsStore(Path.Combine(root, "settings.ini"), NullLogger<IniSettingsStore>.Instance);
            var original = new ScreenshotOptions { AllDisplays = false, RegionShortcut = "Ctrl+Alt+P", WindowShortcut = "F9", FullScreenShortcut = "Shift+F9" };
            await store.SaveAsync(LauraSettings.Default with { Screenshots = original }); Assert.Equal(original, (await store.LoadAsync()).Screenshots);
            Assert.Equal("", (original with { RegionShortcut = "Win+P" }).Sanitized().RegionShortcut);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}

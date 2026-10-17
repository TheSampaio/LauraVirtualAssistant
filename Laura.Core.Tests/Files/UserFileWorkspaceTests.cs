using Laura.Core.Files;

namespace Laura.Core.Tests.Files;

public sealed class UserFileWorkspaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-file-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>Startup creates the default command directory without requiring an earlier generated artifact.</summary>
    [Fact]
    public void CommandWorkspaceExistsBeforeAnyArtifactIsSaved()
    {
        var files = new UserFileWorkspace(_directory);
        Assert.False(Directory.Exists(files.RootDirectory));
        files.EnsureCreated();
        Assert.True(Directory.Exists(files.RootDirectory));
        string existing = Path.Combine(files.RootDirectory, "existing.txt");
        File.WriteAllText(existing, "keep");
        files.EnsureCreated();
        Assert.Equal("keep", File.ReadAllText(existing));
    }
    [Fact]
    public void UserTextNeverBecomesAFilePathAndUnchangedCreationCanBeUndone()
    {
        var files = new UserFileWorkspace(_directory);
        string path = files.SaveNote("../private/secret.txt && erase everything");
        Assert.Equal(_directory, Path.GetDirectoryName(path));
        Assert.EndsWith(".txt", path, StringComparison.Ordinal);
        Assert.Single(files.ListFiles());
        Assert.True(files.Undo());
        Assert.Empty(files.ListFiles());
        Assert.False(files.Undo());
    }

    [Fact]
    public void UndoCannotDeleteAUserEditedFile()
    {
        var files = new UserFileWorkspace(_directory);
        string path = files.SaveNote("Original");
        File.WriteAllText(path, "User changed this");
        Assert.Throws<IOException>(() => files.Undo());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WorkspaceRejectsExecutableArtifactsAndOversizedNotes()
    {
        var files = new UserFileWorkspace(_directory);
        Assert.Throws<ArgumentException>(() => files.SaveArtifact(".exe", [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => files.SaveNote(new string('a', 16001)));
        Assert.Empty(files.ListFiles());
    }

    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
}

using System.Collections.Concurrent;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task EditingOneNoteDoesNotReadUnrelatedDocumentsAndUpdatesTheActualIndex()
    {
        CountingDocumentStorage? storage = null;
        var brain = new BrainService(_settings, new(_root), new BrainIndex(Path.Combine(_root, "incremental"), new Embedding(false)),
            path => storage = new(new FileBrainStorage(path)), NullLogger<BrainService>.Instance, _localizer);
        _services.Add(brain);
        BrainNote first = await brain.SaveAsync(Note("Knowledge/First.md", "First fixture", "Old unique lighthouse value."));
        BrainNote second = await brain.SaveAsync(Note("Knowledge/Second.md", "Second fixture", "Unrelated garden value."));
        storage!.Reads.Clear();
        BrainNote updated = await brain.SaveAsync(first with { Body = "New unique lighthouse value." });
        Assert.False(storage.Reads.ContainsKey(second.Path));
        Assert.Equal(second.Hash, brain.Snapshot.Notes.Single(note => note.Id == second.Id).Hash);
        Assert.Contains("New unique", (await brain.SearchAsync("lighthouse")).Single(hit => hit.Note.Id == first.Id).Excerpt);
        Assert.Contains(brain.Snapshot.Notes, note => note.Status == "archived" && note.Body.Contains("Old unique", StringComparison.Ordinal));
        Assert.Equal(updated.Hash, brain.Snapshot.Notes.Single(note => note.Id == first.Id).Hash);
    }
    [Fact]
    public async Task WatcherReconcilesExternalEditsAndDeletesWithoutLosingOtherKnowledge()
    {
        BrainService brain = Create(); BrainNote first = await brain.SaveAsync(Note("Knowledge/First.md", "First fixture", "Original fixture."));
        BrainNote second = await brain.SaveAsync(Note("Knowledge/Second.md", "Second fixture", "Retained fixture."));
        await File.WriteAllTextAsync(Path.Combine(brain.Location, first.Path), BrainMarkdown.Render(first with { Body = "Externally edited fixture." }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!brain.Snapshot.Notes.Any(note => note.Id == first.Id && note.Body.Contains("Externally edited", StringComparison.Ordinal)))
        { await Task.Delay(50, timeout.Token); await brain.RefreshIfDirtyAsync(timeout.Token); }
        File.Delete(Path.Combine(brain.Location, first.Path));
        while (brain.Snapshot.Notes.Any(note => note.Id == first.Id))
        { await Task.Delay(50, timeout.Token); await brain.RefreshIfDirtyAsync(timeout.Token); }
        Assert.Contains(brain.Snapshot.Notes, note => note.Id == second.Id && note.Hash == second.Hash);
        Assert.DoesNotContain(await brain.SearchAsync("Externally edited"), hit => hit.Note.Id == first.Id);
    }
    private sealed class CountingDocumentStorage(IBrainStorage inner) : IBrainStorage
    {
        internal ConcurrentDictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);
        public string Location => inner.Location;
        public void ValidatePath(string path) => inner.ValidatePath(path);
        public IReadOnlyList<string> EnumerateFiles() => inner.EnumerateFiles();
        public IReadOnlyList<string> EnumerateDirectories() => inner.EnumerateDirectories();
        public string Read(string path) { Reads.AddOrUpdate(path, 1, (_, count) => count + 1); return inner.Read(path); }
        public byte[] ReadBytes(string path) => inner.ReadBytes(path);
        public void WriteBytes(string path, byte[] content) => inner.WriteBytes(path, content);
        public void Write(string path, string content, string? expectedHash) => inner.Write(path, content, expectedHash);
        public void Move(string from, string to) => inner.Move(from, to);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
    }
}

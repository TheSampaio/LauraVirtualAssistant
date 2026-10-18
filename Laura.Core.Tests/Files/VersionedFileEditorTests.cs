using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Files;

namespace Laura.Core.Tests.Files;

/// <summary>Exercises actual atomic filesystem effects and restart recovery in isolated storage.</summary>
public sealed class VersionedFileEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LauraEditTests-" + Guid.NewGuid().ToString("N"));
    /// <summary>Creates isolated source and receipt roots.</summary>
    public VersionedFileEditorTests() => Directory.CreateDirectory(_root);

    /// <summary>A minimal accent correction must preserve layout, scripts, line endings and the original BOM.</summary>
    [Fact]
    public async Task Patch_PreservesUnrelatedMarkupAndEncoding()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        string path = Path.Combine(_root, "site.html");
        string source = "<!doctype html>\r\n<style>.layout{display:grid;gap:24px}</style>\r\n<h1>Configuracao</h1>\r\n<script>const value='\\u00e7';</script>";
        byte[] original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(source)).ToArray();
        await File.WriteAllBytesAsync(path, original);
        var read = await editor.ReadAsync(path);
        var receipt = await editor.PatchAsync(path, read.Hash, "Configuracao", "Configuração");
        byte[] expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(source.Replace("Configuracao", "Configuração", StringComparison.Ordinal))).ToArray();
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        Assert.Equal("Committed", receipt.State);
        Assert.Equal(12, receipt.RemovedCharacters);
        Assert.Equal(12, receipt.AddedCharacters);
    }

    /// <summary>Durable rollback survives a new editor instance and restores the exact bytes.</summary>
    [Fact]
    public async Task Rollback_AfterRestartRestoresExactPreimage()
    {
        string path = Path.Combine(_root, "unicode.txt"), receipts = Path.Combine(_root, "Receipts");
        byte[] original = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Olá\r\noriginal" )).ToArray();
        await File.WriteAllBytesAsync(path, original);
        FileEditReceipt receipt;
        using (var first = new VersionedFileEditor(receipts))
        { var read = await first.ReadAsync(path); receipt = await first.PatchAsync(path, read.Hash, "original", "alterado"); }
        using var restarted = new VersionedFileEditor(receipts);
        Assert.Equal("RolledBack", (await restarted.RollbackAsync(receipt.Id, path)).State);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal("RolledBack", (await restarted.RollbackAsync(receipt.Id, path)).State);
    }

    /// <summary>A concurrent owner's edit is neither overwritten by a stale patch nor removed by stale rollback.</summary>
    [Fact]
    public async Task ExternalChanges_BlockPatchAndRollback()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        string path = Path.Combine(_root, "owner.txt"); await File.WriteAllTextAsync(path, "original");
        var read = await editor.ReadAsync(path);
        await File.WriteAllTextAsync(path, "external edit");
        Assert.Equal("version_changed", (await Assert.ThrowsAsync<FileEditException>(() => editor.PatchAsync(path, read.Hash, "original", "changed"))).Code);
        Assert.Equal("external edit", await File.ReadAllTextAsync(path));
        read = await editor.ReadAsync(path);
        var receipt = await editor.PatchAsync(path, read.Hash, "external edit", "approved edit");
        await File.WriteAllTextAsync(path, "new owner change");
        Assert.Equal("version_changed", (await Assert.ThrowsAsync<FileEditException>(() => editor.RollbackAsync(receipt.Id, path))).Code);
        Assert.Equal("new owner change", await File.ReadAllTextAsync(path));
    }

    /// <summary>A create request never silently becomes an overwrite.</summary>
    [Fact]
    public async Task Create_RefusesExistingFile()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        string path = Path.Combine(_root, "existing.txt"); await File.WriteAllTextAsync(path, "owner content");
        Assert.Equal("existing_file", (await Assert.ThrowsAsync<FileEditException>(() => editor.CreateAsync(path, "destructive replacement"))).Code);
        Assert.Equal("owner content", await File.ReadAllTextAsync(path));
    }

    /// <summary>Ambiguous or absent passages cannot turn into unintended broad replacements.</summary>
    [Theory]
    [InlineData("repeat repeat", "repeat")]
    [InlineData("original", "absent")]
    public async Task Patch_RejectsAmbiguousOrAbsentPassages(string source, string passage)
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        string path = Path.Combine(_root, "source.txt"); await File.WriteAllTextAsync(path, source);
        var read = await editor.ReadAsync(path);
        Assert.Equal("ambiguous_patch", (await Assert.ThrowsAsync<FileEditException>(() => editor.PatchAsync(path, read.Hash, passage, "replacement"))).Code);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    /// <summary>HTML text escapes are rejected while equivalent JavaScript/JSON syntax is left untouched.</summary>
    [Fact]
    public async Task UnicodeEscapes_AreNotGloballyDecoded()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        Assert.Equal("literal_html_escape", (await Assert.ThrowsAsync<FileEditException>(() => editor.CreateAsync(Path.Combine(_root, "bad.html"), @"<p>Configura\u00e7\u00e3o</p>"))).Code);
        string json = "{\"text\":\"\\u00e7\"}";
        string path = Path.Combine(_root, "valid.json"); await editor.CreateAsync(path, json);
        Assert.Equal(json, await File.ReadAllTextAsync(path));
        string html = "<p>Configuração</p><script>const text='\\u00e7';</script>";
        path = Path.Combine(_root, "valid.html"); await editor.CreateAsync(path, html);
        Assert.Equal(html, await File.ReadAllTextAsync(path));
    }

    /// <summary>Only one concurrent patch based on the same preimage may commit.</summary>
    [Fact]
    public async Task ConcurrentPatches_CommitOnlyOneVersion()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        string path = Path.Combine(_root, "race.txt"); await File.WriteAllTextAsync(path, "original");
        var read = await editor.ReadAsync(path);
        Task<FileEditReceipt> first = editor.PatchAsync(path, read.Hash, "original", "first");
        Task<FileEditReceipt> second = editor.PatchAsync(path, read.Hash, "original", "second");
        await first; Assert.Equal("version_changed", (await Assert.ThrowsAsync<FileEditException>(() => second)).Code);
        Assert.Equal("first", await File.ReadAllTextAsync(path));
    }

    /// <summary>A receipt cannot be applied to a different path approved by the tool caller.</summary>
    [Fact]
    public async Task Rollback_RequiresReceiptPathMatch()
    {
        using var editor = new VersionedFileEditor(Path.Combine(_root, "Receipts"));
        var receipt = await editor.CreateAsync(Path.Combine(_root, "one.txt"), "one");
        await Assert.ThrowsAsync<IOException>(() => editor.RollbackAsync(receipt.Id, Path.Combine(_root, "two.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "one.txt")));
    }

    /// <summary>Native aliases include child paths under the real redirected folder and reject escaping traversal.</summary>
    [Fact]
    public void KnownFolders_ResolveChildrenAndRejectEscape()
    {
        var folders = new RedirectedFolders(_root);
        Assert.Equal(Path.Combine(_root, "OneDrive", "Desktop", "site", "index.html"), KnownFolderPaths.Resolve("desktop/site/index.html", folders, _root));
        Assert.Equal(Path.Combine(_root, "CustomDownloads", "hello.cpp"), KnownFolderPaths.Resolve("downloads\\hello.cpp", folders, _root));
        Assert.Throws<IOException>(() => KnownFolderPaths.Resolve("desktop/../../escape.txt", folders, _root));
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>Represents shell redirection without relying on the machine owner's actual folders.</summary>
    private sealed class RedirectedFolders(string root) : IKnownFolderResolver
    {
        /// <inheritdoc />
        public string? Resolve(string alias) => alias switch { "desktop" => Path.Combine(root, "OneDrive", "Desktop"), "downloads" => Path.Combine(root, "CustomDownloads"), _ => null };
    }
}

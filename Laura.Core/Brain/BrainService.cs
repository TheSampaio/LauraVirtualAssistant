using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Text;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>Coordinates Markdown truth, stable graph resolution, local indexing, safe edits and portable vault changes.</summary>
public sealed class BrainService(ISettingsService settings, AssistantStorage appStorage, BrainIndex index,
    Func<string, IBrainStorage> storageFactory, ILogger<BrainService> logger, ILocalizer? localizer = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private IBrainStorage? _storage;
    private BrainSnapshot _snapshot = BrainSnapshot.Empty;
    private FileSystemWatcher? _watcher;
    private Task? _worker;
    private int _dirty = 1;
    /// <summary>Reports coherent changes without exposing note text in diagnostics.</summary>
    public event EventHandler? Changed;
    /// <summary>Gets the current portable vault root.</summary>
    public string Location => _storage?.Location ?? (settings.Current.Brain.Directory.Length > 0 ? settings.Current.Brain.Directory : DefaultLocation);
    /// <summary>Uses the capitalized default while retaining a legacy vault on a case-sensitive filesystem.</summary>
    private string DefaultLocation => Directory.Exists(Path.Combine(appStorage.Directory, "Brain")) || !Directory.Exists(Path.Combine(appStorage.Directory, "brain"))
        ? Path.Combine(appStorage.Directory, "Brain") : Path.Combine(appStorage.Directory, "brain");
    /// <summary>Gets the immutable graph snapshot.</summary>
    public BrainSnapshot Snapshot => Volatile.Read(ref _snapshot);
    /// <summary>Gets whether neural retrieval has a usable installed model.</summary>
    public bool SemanticAvailable => index.SemanticAvailable;
    /// <summary>Initializes lazily and starts debounced watching plus a periodic full rescan for missed sync events.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_storage is not null) { return; }
            _storage = await Task.Run(() => storageFactory(Location), cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                if (!_storage.EnumerateFiles().Contains("README.md", StringComparer.OrdinalIgnoreCase))
                {
                    var readme = new BrainNote { Path = "README.md", Title = "LAURA Brain", Type = "document", Body = "# LAURA Brain\n\nMarkdown is the source of truth. Each note has a stable id and schema_version: 1.\nEdit notes in any Markdown editor. Typed relations target ids; wikilinks resolve titles, aliases and relative paths.\nIndices and embeddings are disposable and stay outside this directory.\nSkills are inert until explicitly enabled; ordinary knowledge never grants tool permissions.\nConflicting drafts and previous revisions are preserved in Archive/.\nCloud sync is supplied by your chosen file/document provider, not by this vault. Plain Markdown is readable to anyone with access.\n" };
                    _storage.Write(readme.Path, BrainMarkdown.Render(readme), null);
                }
                Scan(false);
            }, cancellationToken).ConfigureAwait(false);
            StartWatching();
        }
        catch { _storage = null; throw; }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Reconciles actual file contents and hashes; corruption or deletion of derived indices cannot erase notes.</summary>
    public async Task RefreshAsync(bool rebuild = false, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await Task.Run(() => Scan(rebuild), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Indexes valid files and isolates malformed or conflicting nodes without guessing which version is correct.</summary>
    private void Scan(bool rebuild)
    {
        var notes = new List<BrainNote>(); var issues = new List<BrainIssue>();
        foreach (string path in _storage!.EnumerateFiles().Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string content = _storage.Read(path);
                string hash = FileBrainStorage.Hash(content);
                BrainNote note = Snapshot.Notes.FirstOrDefault(note => note.Path == path && note.Hash == hash) ?? BrainMarkdown.Parse(content, path);
                if (note.Id.Length == 0)
                {
                    note = note with { Id = Guid.NewGuid().ToString("D"), Revision = Guid.NewGuid().ToString("N") };
                    _storage.Write(path, BrainMarkdown.Render(note), note.Hash);
                    note = BrainMarkdown.Parse(_storage.Read(path), path);
                }
                notes.Add(note);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or YamlDotNet.Core.YamlException)
            { issues.Add(new(path, "invalid_or_unreadable")); }
        }
        var duplicates = notes.GroupBy(note => note.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
        var duplicateIds = duplicates.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var group in duplicates) { foreach (BrainNote note in group) { issues.Add(new(note.Path, "conflicting_id")); } }
        BrainNote[] valid = notes.Where(note => !duplicateIds.Contains(note.Id)).ToArray();
        var edges = new List<BrainEdge>(); var ids = valid.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
        foreach (BrainNote note in valid)
        {
            foreach (BrainRelation relation in note.Relations)
            {
                if (ids.Contains(relation.Target)) { edges.Add(new(note.Id, relation.Target, relation.Type, relation.Weight, relation.Confidence, relation.Evidence)); }
                else { issues.Add(new(note.Path, "unresolved_relation")); }
            }
            foreach (string link in BrainMarkdown.Links(note.Body))
            {
                string? target = ResolveLink(link, note.Path, valid);
                if (target is null) { issues.Add(new(note.Path, "ambiguous_or_unresolved_link")); }
                else if (target != note.Id && !edges.Any(edge => edge.Source == note.Id && edge.Target == target))
                { edges.Add(new(note.Id, target, "related_to", .5, .5, "Authored wikilink")); }
            }
        }
        index.Update(Location, valid, rebuild);
        Volatile.Write(ref _snapshot, new(notes, edges, issues)); Interlocked.Exchange(ref _dirty, 0);
    }
    /// <summary>Resolves ids first, then exact portable paths, then unique titles or aliases; ambiguous names remain unresolved.</summary>
    public static string? ResolveLink(string link, string fromPath, IReadOnlyList<BrainNote> notes)
    {
        string key = link.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ? link[3..] : link;
        BrainNote? id = notes.FirstOrDefault(note => note.Id == key); if (id is not null) { return id.Id; }
        string path = key.Replace('\\', '/'); if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { path += ".md"; }
        string relative = (Path.GetDirectoryName(fromPath)?.Replace('\\', '/') is { Length: > 0 } parent ? parent + "/" : "") + path;
        BrainNote[] paths = notes.Where(note => note.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || note.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length == 1) { return paths[0].Id; } if (paths.Length > 1) { return null; }
        BrainNote[] candidates = notes.Where(note => note.Title.Equals(key, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(note.Path).Equals(key, StringComparison.OrdinalIgnoreCase)
            || note.Aliases.Any(alias => alias.Equals(key, StringComparison.OrdinalIgnoreCase) || alias.Equals(path, StringComparison.OrdinalIgnoreCase))).ToArray();
        return candidates.Length == 1 ? candidates[0].Id : null;
    }
    /// <summary>Lists real files, including related attachments and unindexed conflicting documents.</summary>
    public async Task<IReadOnlyList<string>> FilesAsync(CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken).ConfigureAwait(false); return await Task.Run(() => _storage!.EnumerateFiles(), cancellationToken).ConfigureAwait(false); }
    /// <summary>Lists real user-created directories, including empty folders.</summary>
    public async Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken).ConfigureAwait(false); return await Task.Run(() => _storage!.EnumerateDirectories(), cancellationToken).ConfigureAwait(false); }
    /// <summary>Reads a real document rather than rendering cached index contents.</summary>
    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken).ConfigureAwait(false); return await Task.Run(() => _storage!.Read(path), cancellationToken).ConfigureAwait(false); }
    /// <summary>Reads a bounded vault-local attachment through the same link-safe storage adapter.</summary>
    public async Task<byte[]> ReadAttachmentAsync(string path, CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken).ConfigureAwait(false); return await Task.Run(() => _storage!.ReadBytes(path), cancellationToken).ConfigureAwait(false); }
    /// <summary>Resolves a Markdown image relative to its note, refusing URI schemes and traversal beyond the virtual vault root.</summary>
    public static string ResolveAttachment(string ownerPath, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':', StringComparison.Ordinal) || relative.StartsWith('/') || relative.StartsWith('\\')) { throw new IOException("Only vault-local attachments are supported."); }
        var parts = new List<string>((Path.GetDirectoryName(ownerPath)?.Replace('\\', '/') ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (string part in relative.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".") { continue; }
            if (part == "..") { if (parts.Count == 0) { throw new IOException("Attachment leaves the vault."); } parts.RemoveAt(parts.Count - 1); }
            else { parts.Add(part); }
        }
        return string.Join('/', parts);
    }

    /// <summary>Stores one explicitly requested PNG under a content hash and attaches a relative Markdown reference to an exact node or new topic. Similarity never invents relationships.</summary>
    public async Task<BrainNote> AttachVisualAsync(byte[] png, string topic, string? noteId = null, string? expectedLocation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png); ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (png.Length is < 24 or > 32 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) { throw new ArgumentException("Provide a bounded PNG.", nameof(png)); }
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)), height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width == 0 || height == 0 || (ulong)width * height > 32000000) { throw new ArgumentException("PNG dimensions exceed the supported limit.", nameof(png)); }
        topic = topic.Trim(); if (topic.Length > 200) { throw new ArgumentException("Visual topic exceeds its limit.", nameof(topic)); }
        byte[] image = (byte[])png.Clone(); await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        string vault = expectedLocation ?? Location; EnsureLocation(vault);
        BrainNote? note = noteId is null ? _snapshot.Notes.SingleOrDefault(item => item.IsKnowledge && (item.Title.Equals(topic, StringComparison.OrdinalIgnoreCase) || item.Aliases.Contains(topic, StringComparer.OrdinalIgnoreCase)))
            : _snapshot.Notes.SingleOrDefault(item => item.Id == noteId && item.IsKnowledge);
        if (noteId is not null && note is null) { throw new IOException("The selected Brain node is no longer available."); }
        note ??= new BrainNote { Path = "References/" + BrainNaming.FileName(topic, "visual:" + topic), Title = topic, Source = "user_visual_reference", Body = "# " + topic.ReplaceLineEndings(" ") + "\n" };
        string asset = "Assets/Screenshots/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)).ToLowerInvariant() + ".png";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                EnsureLocation(vault);
                _storage!.ValidatePath(asset);
                if (_storage.EnumerateFiles().Contains(asset, StringComparer.OrdinalIgnoreCase))
                { if (!_storage.ReadBytes(asset).AsSpan().SequenceEqual(image)) { throw new IOException("Attachment hash collision or external corruption."); } }
                else { _storage.WriteBytes(asset, image); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        string relative = Path.GetRelativePath(Path.GetDirectoryName(note.Path)?.Length > 0 ? Path.GetDirectoryName(note.Path)! : ".", asset).Replace('\\', '/');
        string label = topic.ReplaceLineEndings(" ").Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
        string reference = "![" + label + "](" + relative + ")";
        if (note.Body.Contains(reference, StringComparison.Ordinal)) { return note; }
        string heading = settings.Current.UiCulture == "pt-BR" ? "## Referências Visuais" : "## Visual References";
        return await SaveAsync(note with { Body = note.Body.TrimEnd() + (note.Body.Contains(heading, StringComparison.Ordinal) ? "\n\n" : "\n\n" + heading + "\n\n") + reference + "\n" }, false, vault, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Saves a note only against its last observed hash, preserving previous revisions and conflicting drafts.</summary>
    public Task<BrainNote> SaveAsync(BrainNote note, CancellationToken cancellationToken = default) => SaveAsync(note, false, cancellationToken);
    /// <summary>Imports authored metadata with its original timestamp when explicitly requested by migration.</summary>
    public Task<BrainNote> SaveAsync(BrainNote note, bool preserveTimestamp, CancellationToken cancellationToken = default) => SaveAsync(note, preserveTimestamp, null, cancellationToken);
    private async Task<BrainNote> SaveAsync(BrainNote note, bool preserveTimestamp, string? expectedLocation, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false); await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (expectedLocation is not null) { EnsureLocation(expectedLocation); }
                _storage!.ValidatePath(note.Path);
                if (note.Hash.Length == 0) { note = note with { Path = DestinationPath(note.Path) }; }
                if (!note.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { throw new IOException("Notes require a Markdown extension."); }
                BrainNote saved = note with { Id = note.Id.Length == 0 ? Guid.NewGuid().ToString("D") : note.Id, UpdatedAt = preserveTimestamp ? note.UpdatedAt : DateTimeOffset.UtcNow,
                    PreviousRevision = note.Revision, Revision = Guid.NewGuid().ToString("N") };
                if (note.Hash.Length > 0)
                {
                    string previousContent = _storage!.Read(note.Path);
                    if (FileBrainStorage.Hash(previousContent) != note.Hash) { PreserveDraft(saved); throw new BrainConflictException(); }
                    try { PreserveRevision(BrainMarkdown.Parse(previousContent, note.Path)); }
                    catch (FormatException)
                    {
                        var original = new BrainNote { Path = "Archive/Revisions/" + Guid.NewGuid().ToString("N") + ".md", Title = "Original document before repair", Status = "archived", Source = "manual_repair",
                            Body = previousContent, Relations = [new(saved.Id, "derived_from", 1, .5, "Original malformed source preserved before user repair", "manual_repair")] };
                        _storage.Write(DestinationPath(original.Path), BrainMarkdown.Render(original), null);
                    }
                }
                try { _storage!.Write(note.Path, BrainMarkdown.Render(saved), note.Hash.Length > 0 ? note.Hash : null); }
                catch (BrainConflictException) { PreserveDraft(saved); throw; }
                Scan(false); return BrainMarkdown.Parse(_storage!.Read(note.Path), note.Path);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private void EnsureLocation(string expected)
    {
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(Location)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new IOException("Brain location changed. Repeat the visual attachment against the newly selected vault."); }
    }
    /// <summary>Keeps the unsaved branch under a new identity with explicit provenance instead of overwriting the other device's version.</summary>
    private void PreserveDraft(BrainNote note)
    {
        var draft = note with { Id = Guid.NewGuid().ToString("D"), Path = "Archive/Conflicts/" + Guid.NewGuid().ToString("N") + ".md", Status = "conflict", Hash = "",
            Relations = [new(note.Id, "derived_from", 1, note.Confidence, "Concurrent edit preserved", "local_editor")] };
        draft = draft with { Body = RebaseImages(note.Body, note.Path, draft.Path) };
        _storage!.Write(DestinationPath(draft.Path), BrainMarkdown.Render(draft), null); Interlocked.Exchange(ref _dirty, 1);
    }
    /// <summary>Preserves authored content before edits; history remains Markdown and survives index deletion.</summary>
    private void PreserveRevision(BrainNote note)
    {
        var revision = note with { Id = Guid.NewGuid().ToString("D"), Path = "Archive/Revisions/" + Guid.NewGuid().ToString("N") + ".md", Status = "archived", Hash = "",
            Relations = [new(note.Id, "derived_from", 1, note.Confidence, "Previous authored revision", note.Source)] };
        revision = revision with { Body = RebaseImages(note.Body, note.Path, revision.Path) };
        _storage!.Write(DestinationPath(revision.Path), BrainMarkdown.Render(revision), null);
    }
    /// <summary>Validates a new destination before applying the directory naming policy.</summary>
    private string DestinationPath(string path, bool directory = false)
    { _storage!.ValidatePath(path); return BrainNaming.DestinationPath(path, _storage.EnumerateDirectories(), directory); }
    private static IEnumerable<string> ImageReferences(string body, string owner)
    {
        foreach (Match match in Regex.Matches(body, @"!\[[^\]]*\]\(([^)]+)\)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            string? asset = null; try { asset = ResolveAttachment(owner, match.Groups[1].Value); } catch (IOException) { }
            if (asset is not null) { yield return asset; }
        }
    }
    private static string RebaseImages(string body, string oldPath, string newPath, string? movedFrom = null, string? movedTo = null) =>
        Regex.Replace(body, @"(!\[[^\]]*\]\()([^)]+)(\))", match =>
        {
            try
            {
                string asset = ResolveAttachment(oldPath, match.Groups[2].Value);
                if (movedFrom is not null && movedTo is not null && (asset.Equals(movedFrom, StringComparison.OrdinalIgnoreCase) || asset.StartsWith(movedFrom + "/", StringComparison.OrdinalIgnoreCase)))
                { asset = movedTo + asset[movedFrom.Length..]; }
                string relative = Path.GetRelativePath(Path.GetDirectoryName(newPath)?.Length > 0 ? Path.GetDirectoryName(newPath)! : ".", asset).Replace('\\', '/');
                return match.Groups[1].Value + relative + match.Groups[3].Value;
            }
            catch (IOException) { return match.Value; }
        }, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    /// <summary>Moves a note while retaining its identity and prior path/name aliases for existing wikilinks.</summary>
    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false); await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                _storage!.ValidatePath(from); _storage.ValidatePath(to);
                to = DestinationPath(to, _storage.EnumerateDirectories().Contains(from, StringComparer.OrdinalIgnoreCase));
                if (to.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)) { throw new IOException("A directory cannot move inside itself."); }
                if (_storage.EnumerateFiles().Any(path => path.Equals(to, StringComparison.OrdinalIgnoreCase) || path.StartsWith(to + "/", StringComparison.OrdinalIgnoreCase)) || _storage.EnumerateDirectories().Contains(to, StringComparer.OrdinalIgnoreCase))
                { throw new IOException("The destination already exists."); }
                if ((from.StartsWith("Assets/Screenshots", StringComparison.OrdinalIgnoreCase) || from.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    && Snapshot.Notes.Any(note => ImageReferences(note.Body, note.Path).Any(asset => asset.Equals(from, StringComparison.OrdinalIgnoreCase) || asset.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase))))
                { throw new IOException("The image is still referenced by a Brain note or revision. Remove its references before moving or archiving it."); }
                var rewritten = new List<(string Path, string Original, string WrittenHash)>();
                try
                {
                foreach (BrainNote note in Snapshot.Notes.Where(note => note.Path == from || note.Path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    string original = _storage.Read(note.Path); BrainNote current = BrainMarkdown.Parse(original, note.Path);
                    if (current.Hash != note.Hash) { throw new BrainConflictException(); }
                    var moved = current with { Aliases = current.Aliases.Concat([note.Path, Path.GetFileNameWithoutExtension(note.Path)]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Body = RebaseImages(current.Body, note.Path, to + note.Path[from.Length..], from, to) };
                    string written = BrainMarkdown.Render(moved); _storage.Write(note.Path, written, current.Hash); rewritten.Add((note.Path, original, FileBrainStorage.Hash(written)));
                }
                _storage!.Move(from, to); Scan(false);
                }
                catch
                {
                    foreach (var previous in rewritten)
                    {
                        try { if (FileBrainStorage.Hash(_storage.Read(previous.Path)) == previous.WrittenHash) { _storage.Write(previous.Path, previous.Original, previous.WrittenHash); } }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning("Image reference rollback unavailable ({ErrorType}).", error.GetType().Name); }
                    }
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>Duplicates content under a fresh identity and explicit derived_from relation.</summary>
    public async Task<BrainNote> DuplicateAsync(BrainNote note, string path, CancellationToken cancellationToken = default) =>
        await SaveAsync(note with { Id = Guid.NewGuid().ToString("D"), Path = path, Hash = "", Revision = "", Aliases = [],
            Body = RebaseImages(note.Body, note.Path, path),
            Relations = [new(note.Id, "derived_from", 1, note.Confidence, "User-created copy", "manual")] }, cancellationToken).ConfigureAwait(false);
    /// <summary>Copies an attachment or folder without overwriting files; copied notes receive fresh identities and remapped internal relations.</summary>
    public async Task CopyPathAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false); await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                _storage!.ValidatePath(from); _storage.ValidatePath(to);
                to = DestinationPath(to, _storage.EnumerateDirectories().Contains(from, StringComparer.OrdinalIgnoreCase));
                if (to.Equals(from, StringComparison.OrdinalIgnoreCase) || to.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)) { throw new IOException("Choose a separate destination."); }
                string[] files = _storage.EnumerateFiles().Where(path => path.Equals(from, StringComparison.OrdinalIgnoreCase) || path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
                string[] folders = _storage.EnumerateDirectories().Where(path => path.Equals(from, StringComparison.OrdinalIgnoreCase) || path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (files.Length == 0 && folders.Length == 0) { throw new FileNotFoundException("Source does not exist."); }
                if (_storage.EnumerateFiles().Any(path => path.Equals(to, StringComparison.OrdinalIgnoreCase) || path.StartsWith(to + "/", StringComparison.OrdinalIgnoreCase)) || _storage.EnumerateDirectories().Contains(to, StringComparer.OrdinalIgnoreCase)) { throw new IOException("The destination already exists."); }
                var notes = files.Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Select(path => BrainMarkdown.Parse(_storage.Read(path), path)).ToArray();
                var ids = notes.Where(note => note.Id.Length > 0).GroupBy(note => note.Id).ToDictionary(group => group.Key, _ => Guid.NewGuid().ToString("D"), StringComparer.Ordinal);
                foreach (string folder in folders) { _storage.CreateDirectory(to + folder[from.Length..]); }
                foreach (string file in files)
                {
                    string target = to + file[from.Length..]; BrainNote? original = notes.FirstOrDefault(note => note.Path == file);
                    if (original is null) { _storage.WriteBytes(target, _storage.ReadBytes(file)); continue; }
                    string body = RebaseImages(original.Body, original.Path, target, from, to);
                    foreach (var pair in ids) { body = body.Replace("[[id:" + pair.Key, "[[id:" + pair.Value, StringComparison.Ordinal); }
                    var copy = original with { Id = original.Id.Length > 0 ? ids[original.Id] : Guid.NewGuid().ToString("D"), Path = target, Aliases = [], Key = "", Hash = "", Revision = Guid.NewGuid().ToString("N"), PreviousRevision = "", Body = body,
                        Relations = original.Relations.Select(relation => relation with { Target = ids.GetValueOrDefault(relation.Target, relation.Target) }).Concat(original.Id.Length > 0 ? [new BrainRelation(original.Id, "derived_from", 1, original.Confidence, "User-created copy", "manual")] : Array.Empty<BrainRelation>()).ToArray() };
                    _storage.Write(target, BrainMarkdown.Render(copy), null);
                }
                Scan(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>Archives a selected file reversibly; references remain resolvable but archived content leaves recall.</summary>
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => MoveAsync(path, "Archive/Deleted/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(path), cancellationToken);
    /// <summary>Creates only a directory explicitly requested by the user.</summary>
    public async Task CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken).ConfigureAwait(false); await Task.Run(() => _storage!.CreateDirectory(DestinationPath(path, true)), cancellationToken).ConfigureAwait(false); Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>Resolves a sync-copy identity conflict only after explicit review, preserving other versions under fresh ids.</summary>
    public async Task ResolveConflictAsync(BrainNote chosen, bool keepBoth, CancellationToken cancellationToken = default)
    {
        BrainNote[] copies = Snapshot.Notes.Where(note => note.Id == chosen.Id && note.Path != chosen.Path).ToArray();
        foreach (BrainNote copy in copies)
        {
            var resolved = copy with { Id = Guid.NewGuid().ToString("D"), Status = keepBoth ? "active" : "archived",
                Relations = copy.Relations.Concat([new BrainRelation(chosen.Id, keepBoth ? "contradicts" : "derived_from", 1, copy.Confidence, "User-reviewed synchronization conflict", "manual_review")]).ToArray() };
            BrainNote saved = await SaveAsync(resolved, cancellationToken).ConfigureAwait(false);
            if (!keepBoth) { await MoveAsync(saved.Path, "Archive/Conflicts/" + Guid.NewGuid().ToString("N") + ".md", cancellationToken).ConfigureAwait(false); }
        }
    }
    /// <summary>Combines bounded lexical, neural, explicit graph, confidence, importance and temporal evidence.</summary>
    public async Task<IReadOnlyList<BrainHit>> SearchAsync(string query, int limit = 20, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var lexical = index.Lexical(query); var semantic = index.Semantic(query); BrainSnapshot snapshot = Snapshot;
            var seeds = lexical.Keys.Concat(semantic.Where(pair => pair.Value > .5).Select(pair => pair.Key)).ToHashSet(StringComparer.Ordinal);
            var graph = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (BrainEdge edge in snapshot.Edges.Where(edge => edge.Type != "contradicts"))
            { if (seeds.Contains(edge.Source)) { graph[edge.Target] = Math.Max(graph.GetValueOrDefault(edge.Target), edge.Weight * edge.Confidence); } if (seeds.Contains(edge.Target)) { graph[edge.Source] = Math.Max(graph.GetValueOrDefault(edge.Source), edge.Weight * edge.Confidence); } }
            BrainOptions options = settings.Current.Brain;
            return (IReadOnlyList<BrainHit>)snapshot.Notes.Where(note => note.IsKnowledge && note.Confidence >= .35 && (since is null || note.UpdatedAt >= since)
                    && !snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id"))
                .Select(note =>
                {
                    double lex = lexical.GetValueOrDefault(note.Id), sem = semantic.GetValueOrDefault(note.Id), neighbor = graph.GetValueOrDefault(note.Id);
                    double score = options.LexicalWeight * lex + options.SemanticWeight * sem + options.GraphWeight * neighbor;
                    if (query.Length == 0) { score = note.Importance * .5; }
                    if (score > 0) { score += options.TemporalWeight / (1 + Math.Max(0, (DateTimeOffset.UtcNow - note.UpdatedAt).TotalDays) / 90); score *= .5 + .3 * note.Confidence + .2 * note.Importance; }
                    return new BrainHit(note, score, lex, sem, neighbor, Excerpt(note.Body, query, 800));
                }).Where(hit => hit.Score > 0).OrderByDescending(hit => hit.Score).Take(Math.Clamp(limit, 1, 100)).ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Produces selective untrusted context under a configured character budget, with dated attribution and unresolved contradictions visible.</summary>
    public async Task<string> RecallAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberConversations) { return ""; }
        if (localizer is not null && Regex.IsMatch(TextNormalizer.Normalize(query), localizer.Get(LocalizationKeys.Brain.OverviewPattern), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) { query = ""; }
        IReadOnlyList<BrainHit> hits = await SearchAsync(query, 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder(); int budget = settings.Current.Brain.ContextCharacters;
        foreach (BrainHit hit in hits)
        {
            string[] conflicts = Snapshot.Edges.Where(edge => edge.Type == "contradicts" && (edge.Source == hit.Note.Id || edge.Target == hit.Note.Id)).Select(edge => edge.Source == hit.Note.Id ? edge.Target : edge.Source).Distinct(StringComparer.Ordinal).Take(4).ToArray();
            string warning = conflicts.Length > 0 ? "Conflicting evidence exists: " + string.Join(", ", conflicts) + ". Do not silently choose a version.\n" : "";
            string piece = $"[{hit.Note.Id}] {hit.Note.Title} ({hit.Note.UpdatedAt:yyyy-MM-dd}, confidence {hit.Note.Confidence.ToString("F2", CultureInfo.InvariantCulture)}, source {hit.Note.Source})\n{warning}{hit.Excerpt}\n";
            if (builder.Length + piece.Length > budget) { builder.Append(piece.AsSpan(0, budget - builder.Length)); break; } builder.Append(piece);
        }
        return builder.ToString();
    }
    /// <summary>Selects an excerpt near actual query text without loading the entire vault into model context.</summary>
    private static string Excerpt(string body, string query, int limit)
    {
        int position = query.Length > 1 ? body.IndexOf(query, StringComparison.OrdinalIgnoreCase) : -1;
        int start = Math.Max(0, position - 100); return body.Substring(start, Math.Min(limit, body.Length - start));
    }
    /// <summary>Switches or copies a vault only after conflict checks and byte verification, retaining all source files.</summary>
    public async Task ChangeLocationAsync(string destination, bool migrate, bool requireExisting, CancellationToken cancellationToken = default)
    {
        if (migrate) { await InitializeAsync(cancellationToken).ConfigureAwait(false); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IBrainStorage target = await Task.Run(() => storageFactory(destination), cancellationToken).ConfigureAwait(false);
            if (Location.Equals(target.Location, StringComparison.OrdinalIgnoreCase)) { throw new IOException("Vault locations must be independent."); }
            if (_storage is FileBrainStorage && target is FileBrainStorage)
            {
                string sourceRoot = Path.GetFullPath(Location), targetRoot = Path.GetFullPath(target.Location);
                if (targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || sourceRoot.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { throw new IOException("Vault directories must not contain each other."); }
            }
            IReadOnlyList<string> targetFiles = await Task.Run(target.EnumerateFiles, cancellationToken).ConfigureAwait(false);
            if (requireExisting && !targetFiles.Any(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))) { throw new IOException("The selected directory contains no Markdown vault."); }
            if (migrate && targetFiles.Count > 0)
            {
                await Task.Run(() =>
                {
                    IReadOnlyList<string> sourceFiles = _storage!.EnumerateFiles();
                    foreach (string path in targetFiles)
                    { if (!sourceFiles.Contains(path, StringComparer.Ordinal) || !_storage.ReadBytes(path).AsSpan().SequenceEqual(target.ReadBytes(path))) { throw new IOException("Migration destination contains conflicting files; nothing was overwritten."); } }
                }, cancellationToken).ConfigureAwait(false);
            }
            if (!migrate && !requireExisting && targetFiles.Count > 0) { throw new IOException("New vault destination must be empty."); }
            if (migrate)
            {
                await Task.Run(() =>
                {
                    foreach (string path in _storage!.EnumerateFiles())
                    { cancellationToken.ThrowIfCancellationRequested(); byte[] data = _storage.ReadBytes(path); if (!targetFiles.Contains(path, StringComparer.Ordinal)) { target.WriteBytes(path, data); } if (!data.AsSpan().SequenceEqual(target.ReadBytes(path))) { throw new IOException("Migration verification failed; original files remain intact."); } }
                    foreach (string directory in _storage.EnumerateDirectories()) { target.CreateDirectory(directory); }
                    IReadOnlyList<string> currentFiles = _storage.EnumerateFiles(), copiedFiles = target.EnumerateFiles();
                    if (!currentFiles.Order(StringComparer.Ordinal).SequenceEqual(copiedFiles.Order(StringComparer.Ordinal))) { throw new IOException("Source changed during migration; keep both folders and retry."); }
                    foreach (string path in currentFiles)
                    { if (!_storage.ReadBytes(path).AsSpan().SequenceEqual(target.ReadBytes(path))) { throw new IOException("Source changed during migration; keep both folders and retry."); } }
                }, cancellationToken).ConfigureAwait(false);
            }
            await settings.UpdateAsync(settings.Current with { Brain = settings.Current.Brain with { Directory = target.Location } }, cancellationToken).ConfigureAwait(false);
            _watcher?.Dispose(); _watcher = null; _storage = null; Volatile.Write(ref _snapshot, BrainSnapshot.Empty);
        }
        finally { _gate.Release(); }
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Exports a private backup only to the user-selected destination; no overwrite or repository packaging occurs.</summary>
    public async Task BackupAsync(string destination, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false); await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (string path in _storage!.EnumerateFiles()) { cancellationToken.ThrowIfCancellationRequested(); using Stream entry = archive.CreateEntry(path, CompressionLevel.Fastest).Open(); entry.Write(_storage.ReadBytes(path)); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>Restores into an empty independently selected vault and validates every archive path through the storage adapter.</summary>
    public async Task RestoreAsync(string backup, string destination, CancellationToken cancellationToken = default)
    {
        IBrainStorage target = storageFactory(destination);
        if (target.EnumerateFiles().Count > 0) { throw new IOException("Restore requires an empty destination."); }
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(backup); long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (entry.FullName.EndsWith('/')) { continue; }
                total += entry.Length; if (entry.Length > 67108864 || total > 536870912 || archive.Entries.Count > 50000) { throw new IOException("Backup exceeds restore limits."); }
                using Stream input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); target.WriteBytes(entry.FullName, bytes.ToArray());
            }
        }, cancellationToken).ConfigureAwait(false);
        await ChangeLocationAsync(destination, false, true, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Watches only filesystem adapters; periodic reconciliation also supports document adapters without native notifications.</summary>
    private void StartWatching()
    {
        _watcher?.Dispose();
        if (_storage is FileBrainStorage)
        {
            _watcher = new FileSystemWatcher(Location) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Changed += MarkDirty; _watcher.Created += MarkDirty; _watcher.Deleted += MarkDirty; _watcher.Renamed += MarkDirty;
            _watcher.Error += (_, _) => Interlocked.Exchange(ref _dirty, 1); _watcher.EnableRaisingEvents = true;
        }
        _worker ??= Task.Run(WatchAsync);
    }
    /// <summary>Debounces provider notifications without doing file I/O on callback threads.</summary>
    private void MarkDirty(object sender, FileSystemEventArgs args) => Interlocked.Exchange(ref _dirty, 1);
    /// <summary>Recovers lost events and transient sync failures, reporting only failure types.</summary>
    private async Task WatchAsync()
    {
        int ticks = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (++ticks % 30 != 0 && Volatile.Read(ref _dirty) == 0) { continue; }
                try { await RefreshAsync(cancellationToken: _lifetime.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
                { logger.LogWarning("Brain reconciliation unavailable ({ErrorType}).", error.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    { _watcher?.Dispose(); await _lifetime.CancelAsync().ConfigureAwait(false); if (_worker is not null) { await _worker.ConfigureAwait(false); } _lifetime.Dispose(); _gate.Dispose(); GC.SuppressFinalize(this); }
}

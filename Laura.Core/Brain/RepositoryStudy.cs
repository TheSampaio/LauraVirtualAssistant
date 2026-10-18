using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.Core.Brain;

/// <summary>Studies large repositories across bounded resumable acquisitions; source content never becomes execution authority.</summary>
public sealed class RepositoryStudy(BrainService brain, BrainSources sources, PermissionManager permissions,
    IRepositoryStudyStore store, IRemoteBrainSource remote)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _lanes = new();
    private static readonly string[] ExcludedDirectories = ["bin", "obj", "node_modules", "vendor", "dist", "build", "packages", "Security", "Credentials"];

    /// <summary>Inventories a reviewed local/public GitHub source, preserving progress or explicitly refreshing versions. Args: source: Existing local directory or public GitHub URL; topic: User objective label; taskId: Trusted native caller task; destination: Actual provider; refresh: Explicit new inventory; cancellationToken: Cancellation. Returns: Actual acquisition coverage, never complete comprehension.</summary>
    public async Task<RepositoryStudyProgress> StartAsync(string source, string topic, Guid taskId, string destination,
        bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 120) { throw new ArgumentException("Use a topic of 1..120 characters."); }
        bool isRemote = source.StartsWith("https://", StringComparison.Ordinal);
        if (!isRemote && !Path.IsPathFullyQualified(source)) { throw new ArgumentException("Repository study requires an absolute local directory or public GitHub URL."); }
        string root = isRemote ? GitHubRepositoryAddress.Parse(source).Url : Path.GetFullPath(source);
        await permissions.AuthorizeRepositoryStudyAsync(taskId, root, destination, cancellationToken).ConfigureAwait(false);
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(root + "\n" + brain.Location)); Guid id = new(identity.AsSpan(0, 16));
        SemaphoreSlim lane = _lanes.GetOrAdd(id, _ => new(1, 1)); await lane.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RepositoryStudyState? prior = await store.ReadAsync(id, cancellationToken).ConfigureAwait(false);
            if (prior is not null && !refresh)
            { EnsureVault(prior); return await permissions.ExecuteAsync(ReadAction(root, isRemote, taskId, destination, "Read existing study coverage."), _ => Task.FromResult(Progress(prior)), cancellationToken).ConfigureAwait(false); }
            var read = ReadAction(root, isRemote, taskId, destination, "Inventory repository metadata; no code or dependencies execute.");
            return await permissions.ExecuteAsync(read, async token =>
            {
                await brain.InitializeAsync(token).ConfigureAwait(false);
                RepositoryStudyState state;
                if (isRemote)
                {
                    var page = remote is IRemoteRepositoryInventory paged ? await paged.BeginInventoryAsync(GitHubRepositoryAddress.Parse(source), new RemoteBrainBudget(), token).ConfigureAwait(false) : null;
                    var inventory = page?.Repository ?? await remote.InspectAsync(GitHubRepositoryAddress.Parse(source), new RemoteBrainBudget(), token).ConfigureAwait(false);
                    var candidates = inventory.Files.Where(file => BrainSources.StudyPath(file.Path) && file.Size is > 0 and <= 65536).Take(10000).ToArray();
                    state = new(id, root, topic, brain.Location, true, inventory.Revision, inventory.Truncated || candidates.Length >= 10000,
                        inventory.Files.Count - candidates.Length + (page?.ExcludedEntries ?? 0), DateTimeOffset.UtcNow, candidates.Select(file => new RepositoryStudyFile(file.Path, file.Size, file.BlobHash)).ToArray(), inventory.License, inventory.Attribution, page?.PendingTrees);
                }
                else { state = await Task.Run(() => InventoryLocal(id, root, topic, brain.Location, token), token).ConfigureAwait(false); }
                if (prior is not null) { state = MergeVersions(state, prior); }
                await store.SaveAsync(state, token).ConfigureAwait(false); await sources.SaveStudyCoverageAsync(state, token).ConfigureAwait(false); return Progress(state);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { lane.Release(); }
    }

    /// <summary>Reads one bounded next batch through current native task/recipient authorization and persists partial coverage.</summary>
    public async Task<RepositoryStudyProgress> ContinueAsync(Guid id, Guid taskId, string destination, string cursor, CancellationToken cancellationToken = default)
    {
        _ = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        SemaphoreSlim lane = _lanes.GetOrAdd(id, _ => new(1, 1)); await lane.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RepositoryStudyState state = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
            await permissions.AuthorizeRepositoryStudyAsync(taskId, state.Source, destination, cancellationToken).ConfigureAwait(false);
            if (cursor != Progress(state).Cursor) { throw new InvalidOperationException("Study cursor is stale; read current coverage before continuing."); }
            if (state.PendingInventory is { Count: > 0 })
            {
                if (remote is not IRemoteRepositoryInventory paged) { throw new InvalidOperationException("The configured repository adapter cannot resume subtree inventory."); }
                return await permissions.ExecuteAsync(ReadAction(state.Source, true, taskId, destination, "Continue the frozen repository inventory through bounded nonrecursive trees."),
                    token => ContinueInventoryAsync(state, paged, token), cancellationToken).ConfigureAwait(false);
            }
            RepositoryStudyFile[] selected = SelectBatch(state);
            if (selected.Length == 0) { return Progress(state); }
            var action = ReadAction(state.Source, state.Remote, taskId, destination, "Acquire the next bounded batch as untrusted Brain references.",
                state.Remote ? [] : selected.Select(file => LocalPath(state.Source, file.Path)).ToArray());
            return await permissions.ExecuteAsync(action, async token =>
            {
                await brain.RefreshIfDirtyAsync(token).ConfigureAwait(false);
                var files = state.Files.ToDictionary(file => file.Path, StringComparer.Ordinal); var documents = new List<BrainRemoteDocument>();
                var budget = new RemoteBrainBudget(); string failure = "";
                try
                {
                    foreach (var file in selected)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            byte[] bytes = state.Remote
                                ? await remote.ReadAsync(new(GitHubRepositoryAddress.Parse(state.Source), state.Revision, state.License, state.Attribution,
                                    state.Files.Where(item => item.Status != "removed").Select(item => new RemoteBrainFile(item.Path, item.Version, item.Size)).ToArray(), state.InventoryTruncated), new(file.Path, file.Version, file.Size), budget, token).ConfigureAwait(false)
                                : await ReadLocalAsync(state.Source, file, token).ConfigureAwait(false);
                            var acquired = await sources.ImportStudyFileAsync(state, file, bytes, token).ConfigureAwait(false); files[file.Path] = acquired;
                            documents.Add(new(file.Path, acquired.NoteId, acquired.NoteHash, acquired.ByteHash, state.Remote ? file.Version : ""));
                        }
                        catch (Exception error) when (error is DecoderFallbackException or JsonException or XmlException or InvalidDataException)
                        { files[file.Path] = file with { Status = "excluded" }; }
                        catch (IOException error)
                        { failure = error is BrainSourceException sourceError ? sourceError.Category + ": " + sourceError.Message : "Source read or storage failed (" + error.GetType().Name + "). Acquired references remain partial; inspect the source before retry."; break; }
                    }
                }
                finally
                {
                    state = state with { Updated = DateTimeOffset.UtcNow, Files = files.Values.ToArray() };
                    // One durable batch may be replayed after a crash, but per-file Markdown identities/hashes make acquisition idempotent.
                    await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
                }
                await sources.SaveStudyCoverageAsync(state, token).ConfigureAwait(false); return Progress(state, documents, failure);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { lane.Release(); }
    }

    /// <summary>Reads private progress only after checking the current task's repository and actual disclosure recipient.</summary>
    public async Task<RepositoryStudyProgress> StatusAsync(Guid id, Guid taskId, string destination, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        await permissions.AuthorizeRepositoryStudyAsync(taskId, state.Source, destination, cancellationToken).ConfigureAwait(false);
        return await permissions.ExecuteAsync(ReadAction(state.Source, state.Remote, taskId, destination, "Read current study coverage."), _ => Task.FromResult(Progress(state)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the user's private checkpoint for native UI only, without repository IO or model disclosure. No model tool exposes this entry point.</summary>
    public async Task<RepositoryStudyProgress> ReadPrivateProgressAsync(Guid id, CancellationToken cancellationToken = default)
        => Progress(await LoadAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Derives honest coverage and a bounded module/language/dependency map from acquired file versions.</summary>
    internal static RepositoryStudyProgress Progress(RepositoryStudyState state, IReadOnlyList<BrainRemoteDocument>? documents = null, string failure = "")
    {
        var current = state.Files.Where(file => file.Status is not ("removed" or "unverified")).ToArray(); int pending = current.Count(file => file.Status == "pending");
        int pendingInventory = state.PendingInventory?.Count ?? 0;
        string cursorInput = state.Revision + "\n" + string.Join('\n', state.Files.Select(file => file.Path + ":" + file.Status + ":" + file.ByteHash))
            + "\n" + string.Join('\n', (state.PendingInventory ?? []).Select(tree => tree.Path + ":" + tree.Hash));
        string cursor = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursorInput)));
        return new(state.Id, state.Source, state.Revision, failure.Length > 0 ? "partial_failure" : pending == 0 && pendingInventory == 0 && !state.InventoryTruncated ? "acquisition_complete" : "partial",
            current.Length, state.InventoryTruncated || pendingInventory > 0, current.Count(file => file.Status == "acquired"), pending, current.Count(file => file.Status == "excluded"),
            state.Files.Count(file => file.Status == "removed"), current.Count(file => file.Changed), documents ?? [],
            current.SelectMany(file => file.Languages).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(16).ToArray(),
            current.SelectMany(file => file.Dependencies).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(128).ToArray(),
            current.GroupBy(file => file.Path.Contains('/') ? file.Path.Split('/')[0] : "(Root)").OrderBy(group => group.Key, StringComparer.Ordinal).Take(64)
                .Select(group => new RepositoryModuleCoverage(group.Key, group.Count(), group.Count(file => file.Status == "acquired"))).ToArray(), failure, state.ExcludedEntries,
            cursor, pendingInventory);
    }

    /// <summary>Advances at most eight frozen subtrees, checkpoints remaining work and never exceeds the original shared request/byte budget.</summary>
    private async Task<RepositoryStudyProgress> ContinueInventoryAsync(RepositoryStudyState state, IRemoteRepositoryInventory paged, CancellationToken cancellationToken)
    {
        var pending = new Queue<RemoteRepositoryTree>(state.PendingInventory ?? []); var files = state.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var budget = new RemoteBrainBudget(); string failure = ""; int excluded = state.ExcludedEntries; bool truncated = state.InventoryTruncated;
        try
        {
            for (int pageNumber = 0; pageNumber < 8 && pending.TryPeek(out var tree); pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Leave room for one maximum-sized next tree response and any bounded redirects.
                if (budget.Bytes > 512000 || budget.Requests > 32) { break; }
                var page = await paged.ReadInventoryPageAsync(new(GitHubRepositoryAddress.Parse(state.Source), state.Revision, state.License, state.Attribution, [], false), tree, budget, cancellationToken).ConfigureAwait(false);
                if (page.Repository.Revision != state.Revision) { throw new BrainSourceException("Inventory changed its frozen revision."); }
                foreach (var file in page.Repository.Files)
                {
                    if (!BrainSources.StudyPath(file.Path) || file.Size is <= 0 or > 65536) { excluded++; continue; }
                    if (files.Count >= 20000 && !files.ContainsKey(file.Path)) { truncated = true; continue; }
                    files.TryGetValue(file.Path, out var existing);
                    if (existing is not null && existing.Status is not ("removed" or "unverified") && existing.Version != file.BlobHash) { throw new BrainSourceException("Frozen inventory contains ambiguous source versions."); }
                    files[file.Path] = new(file.Path, file.Size, file.BlobHash, NoteId: existing?.NoteId ?? "", NoteHash: existing?.NoteHash ?? "", ByteHash: existing?.ByteHash ?? "");
                }
                pending.Dequeue();
                foreach (var child in page.PendingTrees)
                {
                    if (pending.Count >= 10000) { truncated = true; break; }
                    if (!pending.Contains(child)) { pending.Enqueue(child); }
                }
                truncated |= page.Repository.Truncated; excluded += page.ExcludedEntries;
            }
        }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException)
        { failure = error is BrainSourceException sourceError ? sourceError.Category + ": " + sourceError.Message
            : "Frozen subtree inventory stopped: " + error.GetType().Name + ". Completed pages remain checkpointed; inspect rate limits or response bounds before retrying."; }
        finally
        {
            var retained = files.Values.Select(file => pending.Count == 0 && file.Status == "unverified" ? file with { Status = "removed" } : file).ToArray();
            state = state with { Files = retained, PendingInventory = pending.ToArray(), ExcludedEntries = excluded, InventoryTruncated = truncated, Updated = DateTimeOffset.UtcNow };
            await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
        }
        await sources.SaveStudyCoverageAsync(state, cancellationToken).ConfigureAwait(false); return Progress(state, failure: failure);
    }

    /// <summary>Restores bounded operational metadata while pinning the selected Brain identity.</summary>
    private async Task<RepositoryStudyState> LoadAsync(Guid id, CancellationToken cancellationToken)
    { var state = await store.ReadAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Repository study was not found."); EnsureVault(state); return state; }
    /// <summary>Prevents continuation into a newly selected vault without an explicit new study.</summary>
    private void EnsureVault(RepositoryStudyState state)
    { if (!string.Equals(Path.GetFullPath(brain.Location), Path.GetFullPath(state.Vault), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { throw new InvalidOperationException("Brain location changed; start a new study explicitly."); } }

    /// <summary>Freezes native read effects separately from process execution, project edits and other network hosts.</summary>
    private static PermissionAction ReadAction(string source, bool remoteSource, Guid taskId, string destination, string description, string[]? files = null) =>
        new(PermissionOperation.RepositoryStudyRead, "Repository study", description, remoteSource ? [] : files is { Length: > 0 } ? files : [source],
            destination: destination, taskId: taskId, scopeIdentity: source);

    /// <summary>Inventories at most 50,000 entries/10,000 eligible files without a per-acquisition 64-file failure.</summary>
    private static RepositoryStudyState InventoryLocal(Guid id, string root, string topic, string vault, CancellationToken cancellationToken)
    {
        var directories = new Queue<(string Path, int Depth)>(); directories.Enqueue((root, 0)); var files = new List<RepositoryStudyFile>(); int entries = 0, excluded = 0; bool truncated = false;
        while (directories.TryDequeue(out var folder))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder.Path).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > 50000 || files.Count >= 10000) { truncated = true; break; }
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0 || PermissionPolicy.SensitivePath(entry)) { excluded++; continue; }
                if (Directory.Exists(entry))
                {
                    if (folder.Depth >= 24) { truncated = true; excluded++; }
                    else if (Path.GetFileName(entry).StartsWith('.') || ExcludedDirectories.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)) { excluded++; }
                    else { directories.Enqueue((entry, folder.Depth + 1)); }
                    continue;
                }
                string relative = Path.GetRelativePath(root, entry).Replace('\\', '/'); var info = new FileInfo(entry);
                if (!BrainSources.StudyPath(relative) || relative.Length > 1024 || info.Length is <= 0 or > 65536) { excluded++; continue; }
                files.Add(new(relative, (int)info.Length, info.Length + ":" + info.LastWriteTimeUtc.Ticks));
            }
            if (truncated && (entries > 50000 || files.Count >= 10000)) { break; }
        }
        return new(id, root, topic, vault, false, "local-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), truncated, excluded, DateTimeOffset.UtcNow, files);
    }

    /// <summary>Preserves stable identities and removed-file evidence; a refresh re-verifies every current local byte hash in bounded batches.</summary>
    private static RepositoryStudyState MergeVersions(RepositoryStudyState state, RepositoryStudyState prior)
    {
        if (state.PendingInventory is { Count: > 0 })
        { return state with { Files = prior.Files.Select(file => file.Status == "removed" ? file : file with { Status = "unverified", Changed = false }).ToArray() }; }
        var old = prior.Files.ToDictionary(file => file.Path, StringComparer.Ordinal); var current = state.Files.Select(file => old.TryGetValue(file.Path, out var existing)
            ? file with { NoteId = existing.NoteId, NoteHash = existing.NoteHash, ByteHash = existing.ByteHash } : file).ToList();
        var paths = current.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        current.AddRange(prior.Files.Where(file => !paths.Contains(file.Path)).Select(file => file with { Status = "removed" }).Take(Math.Max(0, 20000 - current.Count)));
        return state with { Files = current };
    }

    /// <summary>Selects a deterministic prioritized batch within the original 64/24-file and 256-KiB acquisition limits.</summary>
    private static RepositoryStudyFile[] SelectBatch(RepositoryStudyState state)
    {
        var result = new List<RepositoryStudyFile>(); int bytes = 0;
        foreach (var file in state.Files.Where(file => file.Status == "pending").OrderBy(file => Path.GetFileName(file.Path).Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0
            : Path.GetExtension(file.Path).Equals(".csproj", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file.Path) == "package.json" ? 1 : Path.GetExtension(file.Path) == ".md" ? 2 : 3).ThenBy(file => file.Path, StringComparer.Ordinal))
        {
            if (bytes + file.Size > 262144) { continue; } result.Add(file); bytes += file.Size;
            if (result.Count >= (state.Remote ? 24 : 64)) { break; }
        }
        return result.ToArray();
    }

    /// <summary>Reads one frozen ordinary local file without following redirects or exceeding the source-byte budget.</summary>
    private static async Task<byte[]> ReadLocalAsync(string root, RepositoryStudyFile file, CancellationToken cancellationToken)
    {
        string path = LocalPath(root, file.Path); PermissionPolicy.ValidateResources(new(PermissionOperation.FileReadLocal, "Repository study reader", "Read inventoried source", [path]), []);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var info = new FileInfo(path);
        if (input.Length != file.Size || info.Length + ":" + info.LastWriteTimeUtc.Ticks != file.Version)
        { throw new BrainSourceException("Local source changed since inventory; explicitly refresh the inventory before acquisition. Its pending status was preserved.", "integrity"); }
        byte[] bytes = new byte[file.Size]; await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false); return bytes;
    }
    /// <summary>Resolves a validated relative source without permitting directory traversal, alternate streams or sibling scope escapes.</summary>
    private static string LocalPath(string root, string relative)
    {
        if (!GitHubBrainSource.SafePath(relative)) { throw new InvalidDataException("Invalid repository path."); }
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!PermissionPolicy.Within(path, root)) { throw new InvalidDataException("Repository path escaped its scope."); } return path;
    }
}

using System.Text.Json;

namespace Laura.Core.Brain;

public sealed partial class GitHubBrainSource
{
    /// <inheritdoc />
    public async Task<RemoteRepositoryInventoryPage> BeginInventoryAsync(GitHubRepositoryAddress address, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        string prefix = Prefix(address); using var metadata = await JsonAsync(prefix, 131072, budget, cancellationToken).ConfigureAwait(false);
        if (metadata.RootElement.TryGetProperty("private", out var privateFlag) && privateFlag.GetBoolean()) { throw new BrainSourceException("Private repository authentication is not configured."); }
        string reference = address.Reference.Length > 0 ? address.Reference : metadata.RootElement.GetProperty("default_branch").GetString()!;
        using var commit = await JsonAsync(prefix + "/commits?sha=" + Uri.EscapeDataString(reference) + "&per_page=1", 131072, budget, cancellationToken).ConfigureAwait(false);
        if (commit.RootElement.GetArrayLength() != 1) { throw new BrainSourceException("Repository reference has no commit."); }
        string revision = commit.RootElement[0].GetProperty("sha").GetString()!; ValidateHash(revision);
        string rootTree = commit.RootElement[0].GetProperty("commit").GetProperty("tree").GetProperty("sha").GetString()!; ValidateHash(rootTree);
        string license = metadata.RootElement.TryGetProperty("license", out var licenseData) && licenseData.ValueKind == JsonValueKind.Object
            ? licenseData.GetProperty("spdx_id").GetString() ?? "unknown" : "unknown";
        if (license.Length > 128) { license = "unknown"; }
        return new(new(address, revision, license, address.Owner + "/" + address.Repository, [], false), [new("", rootTree)], 0);
    }

    /// <inheritdoc />
    public async Task<RemoteRepositoryInventoryPage> ReadInventoryPageAsync(RemoteBrainRepository repository, RemoteRepositoryTree tree, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        ValidateHash(repository.Revision); ValidateHash(tree.Hash);
        if (tree.Path.Length > 0 && !SafePath(tree.Path) || tree.Path.Length > 1024) { throw new BrainSourceException("Invalid frozen subtree path."); }
        // Any recursive parameter value enables recursion, including false/0; omission is required by the official API.
        using var page = await JsonAsync(Prefix(repository.Address) + "/git/trees/" + tree.Hash, 1500000, budget, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(page.RootElement.GetProperty("sha").GetString(), tree.Hash, StringComparison.OrdinalIgnoreCase)) { throw new BrainSourceException("Frozen subtree identity mismatch.", "integrity"); }
        var files = new List<RemoteBrainFile>(); var pending = new List<RemoteRepositoryTree>(); int excluded = 0;
        bool truncated = page.RootElement.GetProperty("truncated").GetBoolean();
        foreach (var entry in page.RootElement.GetProperty("tree").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = entry.GetProperty("path").GetString()!;
            if (leaf.Contains('/') || leaf.Contains('\\') || !SafePath(leaf)) { excluded++; continue; }
            string path = tree.Path.Length == 0 ? leaf : tree.Path + "/" + leaf;
            if (!SafePath(path) || path.Length > 1024 || path.Count(character => character == '/') >= 24) { excluded++; truncated = true; continue; }
            string kind = entry.GetProperty("type").GetString()!, mode = entry.GetProperty("mode").GetString()!;
            string hash = entry.GetProperty("sha").GetString()!; ValidateHash(hash);
            if (kind == "tree" && mode == "040000")
            {
                if (path.Split('/').Any(segment => segment.StartsWith('.') || segment is "node_modules" or "bin" or "obj" or "Security" or "Credentials")) { excluded++; continue; }
                if (pending.Count >= 10000) { truncated = true; break; } pending.Add(new(path, hash));
            }
            else if (kind == "blob" && mode is "100644" or "100755" && entry.TryGetProperty("size", out var size) && size.TryGetInt32(out int bytes)
                && bytes is > 0 and <= 65536 && BrainSources.StudyPath(path))
            { if (files.Count >= 10000) { truncated = true; break; } files.Add(new(path, hash, bytes)); }
            else { excluded++; }
        }
        return new(repository with { Files = files, Truncated = truncated }, pending, excluded);
    }
}

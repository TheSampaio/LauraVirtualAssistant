namespace Laura.Core.Brain;

/// <summary>Private operational inventory; durable source knowledge remains in attributed Markdown references.</summary>
public sealed record RepositoryStudyState(Guid Id, string Source, string Topic, string Vault, bool Remote, string Revision,
    bool InventoryTruncated, int ExcludedEntries, DateTimeOffset Updated, IReadOnlyList<RepositoryStudyFile> Files,
    string License = "unknown", string Attribution = "", IReadOnlyList<RemoteRepositoryTree>? PendingInventory = null)
{
    /// <summary>Validates bounded metadata before use; stored state never authorizes source IO.</summary>
    public RepositoryStudyState Validated()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Source) || Source.Length > 2048 || string.IsNullOrWhiteSpace(Vault)
            || string.IsNullOrWhiteSpace(Topic) || Topic.Length > 120 || Revision is null || Revision.Length > 120 || Files is null || Files.Count > 20000
            || License is null || License.Length > 128 || Attribution is null || Attribution.Length > 512
            || ExcludedEntries < 0 || Files.Any(file => file is null || !GitHubBrainSource.SafePath(file.Path) || file.Path.Length > 1024
                || file.Size is < 1 or > 65536 || file.Version is null || file.Version.Length > 120
                || file.Status is not ("pending" or "acquired" or "excluded" or "removed" or "unverified") || file.NoteId is null || file.NoteHash is null || file.ByteHash is null
                || file.NoteId.Length > 80 || file.NoteHash.Length > 128 || file.ByteHash.Length > 64 || file.Languages.Count > 16 || file.Dependencies.Count > 128)
            || Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != Files.Count)
        { throw new InvalidDataException("Repository study inventory is invalid or exceeds its bounds."); }
        if (PendingInventory is { Count: > 10000 } || PendingInventory?.Any(tree => tree is null || tree.Path is null || tree.Hash is null || tree.Path.Length > 1024 || tree.Path.Length > 0 && !GitHubBrainSource.SafePath(tree.Path)
            || tree.Hash.Length != 40 || !tree.Hash.All(char.IsAsciiHexDigit)) == true) { throw new InvalidDataException("Repository subtree inventory is invalid."); }
        if (Remote) { _ = GitHubRepositoryAddress.Parse(Source); }
        else if (!Path.IsPathFullyQualified(Source) || Permissions.PermissionPolicy.IsNetworkPath(Source)) { throw new InvalidDataException("Study source must be a local directory or public GitHub repository."); }
        return this with { Files = Files.ToArray() };
    }
}

/// <summary>One source version and its actual acquisition disposition; no body or executable instructions are cached here.</summary>
public sealed record RepositoryStudyFile(string Path, int Size, string Version, string Status = "pending", string NoteId = "", string NoteHash = "", string ByteHash = "",
    IReadOnlyList<string>? ObservedLanguages = null, IReadOnlyList<string>? DeclaredDependencies = null, bool Changed = false)
{
    /// <summary>Gets languages identified by actual parsing, separately from model comprehension.</summary>
    public IReadOnlyList<string> Languages => ObservedLanguages ?? [];
    /// <summary>Gets manifest declarations, never installed dependency claims.</summary>
    public IReadOnlyList<string> Dependencies => DeclaredDependencies ?? [];
}

/// <summary>Honest acquisition coverage with a bounded set of real attributed documents for model interpretation.</summary>
public sealed record RepositoryStudyProgress(Guid Id, string Source, string Revision, string State, int InventoryFiles, bool InventoryTruncated,
    int AcquiredFiles, int PendingFiles, int ExcludedFiles, int RemovedFiles, int ChangedFiles,
    IReadOnlyList<BrainRemoteDocument> Documents, IReadOnlyList<string> Languages, IReadOnlyList<string> DeclaredDependencies,
    IReadOnlyList<RepositoryModuleCoverage> Modules, string Failure = "", int ExcludedEntries = 0, string Cursor = "", int PendingInventoryDirectories = 0);

/// <summary>Observed module coverage derived from paths and imported file versions, not invented architecture.</summary>
public sealed record RepositoryModuleCoverage(string Module, int Files, int Acquired);

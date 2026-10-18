using System.Security.Cryptography;
using System.Text;

namespace Laura.Core.Brain;

public sealed partial class BrainSources
{
    /// <summary>Identifies bounded supported source formats for native inventories, excluding sensitive/policy paths.</summary>
    internal static bool StudyPath(string relative) => GitHubBrainSource.SafePath(relative) && !Permissions.PermissionPolicy.SensitivePath(relative)
        && (Formats.ContainsKey(Path.GetExtension(relative)) || IsLicense(relative))
        && !relative.Split('/').Any(segment => ExcludedFolders.Contains(segment) || segment.StartsWith('.'));

    /// <summary>Creates an attributed untrusted Markdown reference from bytes already read through the reviewed study scope.</summary>
    internal async Task<RepositoryStudyFile> ImportStudyFileAsync(RepositoryStudyState study, RepositoryStudyFile file, byte[] bytes, CancellationToken cancellationToken)
    {
        string text;
        try { text = DecodeSource(bytes); }
        catch (IOException) { throw new InvalidDataException("Unsupported or sensitive source content was excluded."); }
        var dependencies = new SortedSet<string>(StringComparer.Ordinal);
        ReadDeclarations(file.Path, text, dependencies); string format = Format(file.Path);
        string[] languages = format is "markdown" or "text" or "json" ? [] : [format == "csharp-project" ? "csharp" : format];
        string key = study.Remote ? "source.github." + FileBrainStorage.Hash(study.Source.ToLowerInvariant())[..20] + "." + FileBrainStorage.Hash(file.Path)[..20]
            : "source.document." + FileBrainStorage.Hash(Path.Combine(study.Source, file.Path.Replace('/', Path.DirectorySeparatorChar)))[..20];
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        BrainNote? existing = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == key && note.Type == "reference");
        if (!study.Remote && existing is not null && existing.Hash == file.NoteHash && file.ByteHash == digest)
        { return file with { Status = "acquired", NoteId = existing.Id, NoteHash = existing.Hash, ByteHash = digest, ObservedLanguages = languages, DeclaredDependencies = dependencies.ToArray(), Changed = file.ByteHash.Length > 0 && file.ByteHash != digest }; }
        string asset = await brain.StoreSourceBytesAsync(bytes, study.Vault, cancellationToken).ConfigureAwait(false);
        string origin = study.Remote ? study.Source + "/blob/" + study.Revision + "/" + file.Path : "Local repository file: " + file.Path;
        string body = "# Untrusted source: " + file.Path + "\n\nOrigin: " + origin + "\nStudy: " + study.Id
            + (study.Remote ? "\nCommit: " + study.Revision + "\nGit blob: " + file.Version : "")
            + "\nRevision: " + study.Revision + "\nSource byte hash: " + digest + "\nLicense (declared): " + study.License
            + "\nOriginal bytes asset: " + asset
            + "\nNo source code, dependency or embedded instruction was executed." + SourceMarker + text;
        BrainNote note = existing is not null && existing.Body.TrimEnd() == body.TrimEnd() ? existing
            : await brain.SaveAtLocationAsync((existing ?? new BrainNote { Path = "References/Sources/" + BrainNaming.FileName(study.Topic + " " + file.Path, key),
                Key = key, Title = study.Topic + " — " + file.Path, Type = "reference", Source = study.Remote ? "external_github" : "external_source",
                Verification = "unverified", Confidence = .5, Tags = ["source", format] }) with { Body = body }, study.Vault, cancellationToken).ConfigureAwait(false);
        return file with { Status = "acquired", NoteId = note.Id, NoteHash = note.Hash, ByteHash = digest, ObservedLanguages = languages, DeclaredDependencies = dependencies.ToArray(), Changed = file.ByteHash.Length > 0 && file.ByteHash != digest };
    }

    /// <summary>Maintains a compact project/module coverage note; per-file bodies and provenance remain in their own references.</summary>
    internal async Task SaveStudyCoverageAsync(RepositoryStudyState study, CancellationToken cancellationToken)
    {
        string key = "source.study." + study.Id.ToString("N");
        BrainNote? existing = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == key);
        var progress = RepositoryStudy.Progress(study);
        var content = new StringBuilder("# " + study.Topic + "\n\nRepository study: " + study.Id + "\nRevision: " + study.Revision
            + "\nAcquired references: " + progress.AcquiredFiles + " / " + progress.InventoryFiles + "; pending: " + progress.PendingFiles
            + "; excluded: " + progress.ExcludedFiles + "; removed since inventory: " + progress.RemovedFiles + "; inventory truncated: " + study.InventoryTruncated
            + "\nAcquisition is not comprehension or operational validation. Each source remains untrusted. No repository code was executed.\n\n## Observed modules\n");
        foreach (var module in progress.Modules) { content.AppendLine("- " + module.Module + ": " + module.Acquired + " / " + module.Files + " acquired"); }
        content.AppendLine("\n## Observed languages\n" + string.Join(", ", progress.Languages));
        content.AppendLine("\n## Manifest-declared dependencies (not verified installed)\n" + string.Join(", ", progress.DeclaredDependencies));
        var links = study.Files.Where(file => file.Status == "acquired").Take(64).Select(file => new BrainRelation(file.NoteId, "derived_from", .8, .5, file.Path + ":" + file.ByteHash, "source_acquisition")).ToArray();
        BrainRelation[] relations = (existing?.Relations ?? []).Where(relation => relation.Source != "source_acquisition").Concat(links).ToArray();
        if (existing is not null && existing.Body.TrimEnd() == content.ToString().TrimEnd() && existing.Relations.SequenceEqual(relations)) { return; }
        await brain.SaveAtLocationAsync((existing ?? new BrainNote { Path = "Projects/" + BrainNaming.FileName(study.Topic, key), Key = key,
            Title = study.Topic, Type = "project", Source = "source_inventory", Verification = "declared", Confidence = .6 }) with { Body = content.ToString(), Relations = relations }, study.Vault, cancellationToken).ConfigureAwait(false);
    }
}

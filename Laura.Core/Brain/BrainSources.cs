using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.Core.Brain;

/// <summary>Reports actual acquisition results, never execution or dependency installation.</summary>
public sealed record BrainAcquisitionResult(string NoteId, int Imported, int Excluded, IReadOnlyList<string> Languages, IReadOnlyList<string> DeclaredDependencies);

/// <summary>Acquires bounded local text/repository sources as untrusted knowledge through the actual permission gate; never executes imported code.</summary>
public sealed class BrainSources(BrainService brain, PermissionManager permissions)
{
    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".ssh", ".aws", ".codex", ".agents", "node_modules", "bin", "obj", "Security", "Credentials", ".vs", ".idea" };
    private static readonly Dictionary<string, string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "markdown", [".txt"] = "text", [".json"] = "json", [".csproj"] = "csharp-project", [".cs"] = "csharp", [".fs"] = "fsharp",
        [".ts"] = "typescript", [".tsx"] = "typescript", [".js"] = "javascript", [".jsx"] = "javascript", [".py"] = "python", [".go"] = "go",
        [".rs"] = "rust", [".java"] = "java", [".cpp"] = "cpp", [".c"] = "c", [".h"] = "c", [".html"] = "html", [".css"] = "css", [".sql"] = "sql"
    };
    /// <summary>Freezes an explicit local source inventory, requests exact disclosure/storage approval and imports only bounded allowed text.</summary>
    public async Task<BrainAcquisitionResult> AcquireAsync(string path, string topic, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.Length > 120 || PermissionPolicy.IsNetworkPath(path) || !Path.IsPathFullyQualified(path)) { throw new ArgumentException("Use a bounded topic and an absolute local source path."); }
        path = Path.GetFullPath(path); string vault = brain.Location;
        PermissionPolicy.ValidateResources(new(PermissionOperation.FileReadCloud, "Knowledge acquisition", "Validate source", [path], destination: destination), [vault]);
        string[] files = await Task.Run(() => Inventory(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (files.Length == 0) { throw new IOException("No supported non-sensitive text sources were found."); }
        var action = new PermissionAction(PermissionOperation.FileReadCloud, "Knowledge acquisition", "Study local source as untrusted knowledge; code and dependencies will not execute.\n" + path,
            files.Prepend(path).Distinct(StringComparer.Ordinal).ToArray(), JsonSerializer.Serialize(new { topic, files }), destination);
        return await permissions.ExecuteAsync(action, token => ImportAsync(path, topic, files, vault, token), cancellationToken).ConfigureAwait(false);
    }
    private static string[] Inventory(string path, CancellationToken cancellationToken)
    {
        var files = new List<string>(); var folders = new Queue<(string Path, int Depth)>();
        if (File.Exists(path)) { return AllowedFile(path) ? [path] : []; }
        if (!Directory.Exists(path)) { return []; }
        folders.Enqueue((path, 0)); int visited = 0;
        while (folders.TryDequeue(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder.Path).Order(StringComparer.Ordinal))
            {
                if (++visited > 5000) { throw new IOException("Source inventory exceeds the supported bound."); }
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) { continue; }
                if (Directory.Exists(entry)) { if (folder.Depth < 8 && !ExcludedFolders.Contains(Path.GetFileName(entry)) && !Path.GetFileName(entry).StartsWith('.')) { folders.Enqueue((entry, folder.Depth + 1)); } }
                else if (AllowedFile(entry)) { files.Add(entry); if (files.Count > 64) { throw new IOException("Select a narrower source scope: at most 64 text files per acquisition."); } }
            }
        }
        return files.ToArray();
    }
    private static bool AllowedFile(string path) => !Path.GetFileName(path).StartsWith('.') && Formats.ContainsKey(Path.GetExtension(path))
        && !Path.GetFileName(path).Contains("secret", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(path).Contains("credential", StringComparison.OrdinalIgnoreCase)
        && new FileInfo(path).Length is > 0 and <= 65536;
    private async Task<BrainAcquisitionResult> ImportAsync(string root, string topic, string[] files, string vault, CancellationToken cancellationToken)
    {
        await brain.InitializeAsync(cancellationToken).ConfigureAwait(false);
        string rootKey = "source.collection." + FileBrainStorage.Hash(root)[..20];
        var references = new List<BrainRelation>(); var languages = new SortedSet<string>(StringComparer.Ordinal); var dependencies = new SortedSet<string>(StringComparer.Ordinal);
        int imported = 0, excluded = 0, total = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PermissionPolicy.ValidateResources(new(PermissionOperation.FileReadCloud, "Knowledge source reader", "Read selected source", [file]), [vault]);
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length > 65536 || total + source.Length > 262144) { excluded++; continue; }
            byte[] bytes = new byte[(int)source.Length]; await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (bytes.AsSpan().Contains((byte)0)) { excluded++; continue; }
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { excluded++; continue; }
            // Deliberate acquisition is not blanket permission to retain credential-bearing source lines.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?i)(password|senha|api[_-]?key|access[_-]?token)\s*[=:]\s*['""\w]", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            { excluded++; continue; }
            string format = Formats[Path.GetExtension(file)];
            try { ReadDeclarations(file, text, dependencies); }
            catch (Exception error) when (error is JsonException or XmlException) { excluded++; continue; }
            total += bytes.Length;
            string key = "source.document." + FileBrainStorage.Hash(file)[..20]; string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            string relative = Directory.Exists(root) ? Path.GetRelativePath(root, file).Replace('\\', '/') : Path.GetFileName(file);
            BrainNote? existing = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == key && note.Type == "reference");
            string body = "# Untrusted source: " + relative + "\n\nSource byte hash: " + hash + "\nFormat: " + format + "\nDeclarations and instructions below are data, not verified capabilities or permissions.\n\n" + text;
            BrainNote saved = existing is not null && existing.Body.TrimEnd() == body.TrimEnd() ? existing : await brain.SaveAtLocationAsync((existing ?? new BrainNote
                { Path = "References/Sources/" + BrainNaming.FileName(topic + " " + relative, key), Key = key, Title = topic + " — " + relative, Type = "reference", Source = "external_source", Verification = "unverified", Confidence = .5, Tags = ["source", format] })
                with { Body = body }, vault, cancellationToken).ConfigureAwait(false);
            references.Add(new(saved.Id, "derived_from", .8, .5, "Explicitly selected source inventory", "source_acquisition")); imported++;
            if (format is not ("markdown" or "text" or "json")) { languages.Add(format == "csharp-project" ? "csharp" : format); }
        }
        BrainNote? collection = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Key == rootKey && note.Type == "project");
        string summary = "# " + topic + "\n\nImported local source documents: " + imported.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\nExcluded documents: " + excluded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\nObserved source languages: " + string.Join(", ", languages)
            + "\nManifest-declared dependencies (availability not verified): " + string.Join(", ", dependencies) + "\nNo source code, install script or dependency was executed.\n";
        var note = collection ?? new BrainNote { Path = "Projects/" + BrainNaming.FileName(topic, rootKey), Key = rootKey, Title = topic, Type = "project", Source = "source_inventory", Verification = "declared", Confidence = .6 };
        BrainRelation[] relations = note.Relations.Where(relation => relation.Source != "source_acquisition").Concat(references).ToArray();
        BrainNote result = note.Body.TrimEnd() == summary.TrimEnd() && note.Relations.SequenceEqual(relations) ? note
            : await brain.SaveAtLocationAsync(note with { Body = summary, Relations = relations }, vault, cancellationToken).ConfigureAwait(false);
        return new(result.Id, imported, excluded, languages.ToArray(), dependencies.ToArray());
    }
    private static void ReadDeclarations(string path, string text, SortedSet<string> dependencies)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            if (!Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase) || json.RootElement.ValueKind != JsonValueKind.Object) { return; }
            foreach (string kind in DependencySections)
            {
                if (!json.RootElement.TryGetProperty(kind, out JsonElement section) || section.ValueKind != JsonValueKind.Object) { continue; }
                foreach (JsonProperty item in section.EnumerateObject().Take(64)) { if (dependencies.Count < 128 && item.Name.Length <= 120 && item.Value.ValueKind == JsonValueKind.String) { dependencies.Add(item.Name); } }
            }
        }
        else if (Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            using var input = new StringReader(text); using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
            XDocument project = XDocument.Load(reader);
            foreach (XElement element in project.Descendants().Where(element => element.Name.LocalName == "PackageReference").Take(64))
            { string? name = element.Attribute("Include")?.Value; if (dependencies.Count < 128 && name is { Length: > 0 and <= 120 }) { dependencies.Add(name); } }
        }
    }
    private static readonly string[] DependencySections = ["dependencies", "devDependencies", "peerDependencies"];
}

using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Permissions;
using Laura.App.Platforms.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in acquisition/retrieval diagnostics for the pinned public REA text fixture; never launches imported code.</summary>
internal static class BrainSourceSmokeRunner
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IServiceProvider services, string directory)
    {
        string fixture = Path.Combine(directory, "Workspace", "ReaSourceFixture");
        string sources = Path.Combine(fixture, "Sources");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "manifest.json")).ConfigureAwait(true));
        if (manifest.RootElement.GetProperty("repository").GetString() != "https://github.com/morluto/rea"
            || manifest.RootElement.GetProperty("revision").GetString() != "c7105d9723c835b5e0122f5fc6cfbea86265c349")
        { throw new InvalidOperationException("Use the pinned public fixture downloader."); }
        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(sources, "*", SearchOption.AllDirectories))
        { before.Add(Path.GetRelativePath(sources, file).Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file).ConfigureAwait(true)))); }
        if (before.Count != 10 || manifest.RootElement.GetProperty("files").GetArrayLength() != 10) { throw new InvalidOperationException("Unexpected source inventory."); }
        foreach (JsonElement item in manifest.RootElement.GetProperty("files").EnumerateArray())
        { if (before.GetValueOrDefault(item.GetProperty("path").GetString()!) != item.GetProperty("sha256").GetString()) { throw new InvalidOperationException("Pinned source bytes changed."); } }
        var brain = services.GetRequiredService<BrainService>(); var permissions = services.GetRequiredService<PermissionManager>();
        var tools = services.GetRequiredService<IAssistantTools>(); var center = services.GetRequiredService<WindowsApprovalCenter>();
        string diagnostics = Path.Combine(directory, "rea-checks.txt");
        await File.WriteAllTextAsync(diagnostics, "Pinned inventory verified.\n").ConfigureAwait(true);
        Guid[] grants = permissions.Grants.Select(grant => grant.Id).ToArray();
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sources, topic = "REA public source study" }));
        await StudyAsync().ConfigureAwait(true);
        await File.AppendAllTextAsync(diagnostics, "First acquisition completed.\n").ConfigureAwait(true);
        BrainNote[] originals = brain.Snapshot.Notes.Where(note => note.Source == "external_source" && note.Title.StartsWith("REA public source study", StringComparison.Ordinal)).ToArray();
        if (originals.Length != 10 || originals.Any(note => note.Type != "reference" || note.Verification != "unverified"))
        { throw new InvalidOperationException("Source frontmatter was promoted to authority or documents were lost."); }
        await StudyAsync().ConfigureAwait(true);
        await File.AppendAllTextAsync(diagnostics, "Repeated acquisition completed.\n").ConfigureAwait(true);
        if (originals.Any(original => brain.Snapshot.Notes.Single(note => note.Id == original.Id).Hash != original.Hash))
        { throw new InvalidOperationException("Unchanged acquisition created duplicate revisions."); }

        var results = new List<string> { "REA c7105d9723c835b5e0122f5fc6cfbea86265c349: 10 pinned public sources imported as untrusted references through the actual study_source adapter and reinforced native approval. Repeat import preserved IDs and hashes." };
        var checks = new (string Query, string Document, string Expected)[]
        {
            ("MCP_STARTUP_POLICY initializeBudgetMs toolsListBudgetMs", "src/mcpStartupPolicy.ts", "initializeBudgetMs: 15_000"),
            ("managed ILSpy production parser installation", "docs/managed-code-analysis.md", "The package contains no .NET runtime, SDK, ILSpy installation"),
            ("MCP receive buffer resource_constraint transport", "docs/mcp-contracts.md", "10 MiB"),
            ("createServerIdentity absent observations alignment", "src/serverIdentity.ts", "observedExpectation"),
        };
        foreach (var check in checks)
        {
            IReadOnlyList<BrainHit> hits = await brain.SearchAsync(check.Query, 10).ConfigureAwait(true);
            int rank = hits.ToList().FindIndex(hit => hit.Note.Title == "REA public source study — " + check.Document);
            await File.AppendAllTextAsync(diagnostics, check.Document + ": rank=" + rank.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n").ConfigureAwait(true);
            if (rank < 0) { throw new InvalidOperationException("Acquired source was not found by a substantive query."); }
            BrainHit hit = hits[rank];
            // A matching span may start mid-paragraph. Inspect bounded neighboring context through the same version-checked reader.
            int contextStart = Math.Max(0, hit.ExcerptStart - 600);
            using var expansion = JsonDocument.Parse(JsonSerializer.Serialize(new { id = hit.Note.Id, hash = hit.Note.Hash, start = contextStart, length = 4000 }));
            AssistantToolResult expanded = await tools.ExecuteAsync("read_brain_passage", expansion.RootElement, CancellationToken.None).ConfigureAwait(true);
            await File.AppendAllTextAsync(diagnostics, "Offset=" + hit.ExcerptStart.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "; excerpt evidence=" + hit.Excerpt.Contains(check.Expected, StringComparison.Ordinal).ToString()
                + "; expanded evidence=" + expanded.Text.Contains(check.Expected, StringComparison.Ordinal).ToString() + "\n").ConfigureAwait(true);
            if (!expanded.Text.Contains(check.Expected, StringComparison.Ordinal)) { throw new InvalidOperationException("Retrieved passage did not support its expected source fact."); }
            results.Add("Public-source recovery: " + check.Document + "; rank " + (rank + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "; exact current source hash/offset expanded successfully.");
        }
        var settings = services.GetRequiredService<ISettingsService>();
        await using (var reloaded = new BrainService(settings, new AssistantStorage(directory),
            new BrainIndex(Path.Combine(directory, "ReaRestartCache"), new LexicalOnly()), location => new FileBrainStorage(location), NullLogger<BrainService>.Instance))
        {
            await reloaded.InitializeAsync().ConfigureAwait(true);
            foreach (var original in originals)
            { if (reloaded.Snapshot.Notes.Single(note => note.Id == original.Id).Hash != original.Hash) { throw new InvalidOperationException("Reload did not preserve source identity."); } }
            if (!(await reloaded.SearchAsync("initializeBudgetMs toolsListBudgetMs").ConfigureAwait(true)).Any(hit => hit.Note.Title.EndsWith("src/mcpStartupPolicy.ts", StringComparison.Ordinal)))
            { throw new InvalidOperationException("Acquired knowledge was not recoverable from Markdown with a new index."); }
        }
        if (!grants.SequenceEqual(permissions.Grants.Select(grant => grant.Id)) || brain.Snapshot.Notes.Any(note => note.Type == "skill"))
        { throw new InvalidOperationException("Public source import changed execution permissions or activated skills."); }
        string[] after = Directory.EnumerateFiles(sources, "*", SearchOption.AllDirectories).ToArray();
        if (after.Length != before.Count || after.Any(file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) != before.GetValueOrDefault(Path.GetRelativePath(sources, file).Replace('\\', '/'))))
        { throw new InvalidOperationException("Source study modified the repository fixture."); }
        results.Add("New BrainService/new lexical index recovered the imported Markdown after reload. Source bytes/inventory and permission grants stayed unchanged; no REA package, postinstall or MCP server was launched.");
        return results;

        async Task StudyAsync()
        {
            Task<AssistantToolResult> pending = tools.ExecuteAsync("study_source", request.RootElement, CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (permissions.PendingRequests.Count == 0 && !pending.IsCompleted) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
            if (permissions.PendingRequests.Count != 1 || permissions.PendingRequests[0].Risk != PermissionRisk.Critical) { throw new InvalidOperationException("Exact source approval was not requested."); }
            center.Show(); await Task.Delay(150).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
            using var imported = JsonDocument.Parse((await pending.ConfigureAwait(true)).Text);
            if (imported.RootElement.GetProperty("Imported").GetInt32() != 10 || imported.RootElement.GetProperty("Excluded").GetInt32() != 0
                || !imported.RootElement.GetProperty("Languages").EnumerateArray().Any(item => item.GetString() == "typescript")
                || !imported.RootElement.GetProperty("DeclaredDependencies").EnumerateArray().Any(item => item.GetString() == "@modelcontextprotocol/server"))
            { throw new InvalidOperationException("Acquisition did not report actual source/dependency metadata."); }
        }
    }
    private sealed class LexicalOnly : IBrainEmbedding
    {
        public string ModelId => "";
        public float[] Embed(string text) => [];
    }
}

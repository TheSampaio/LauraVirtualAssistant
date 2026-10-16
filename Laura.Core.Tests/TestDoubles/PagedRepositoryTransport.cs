using System.Net;
using System.Text;
using System.Text.Json;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Emulates the official metadata/commit/nonrecursive-tree protocol with 6,000 source entries and no network.</summary>
internal sealed class PagedRepositoryTransport : HttpMessageHandler
{
    internal static readonly string Commit = new('c', 40), RootTree = new('e', 40);
    internal List<Uri> Requests { get; } = [];
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var uri = request.RequestUri!; Requests.Add(uri);
        Assert.Equal("api.github.com", uri.Host); Assert.Null(request.Headers.Authorization); Assert.DoesNotContain("recursive", uri.Query, StringComparison.Ordinal);
        object payload;
        if (uri.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal)) { payload = new[] { new { sha = Commit, commit = new { tree = new { sha = RootTree } } } }; }
        else if (uri.AbsolutePath.Contains("/git/trees/", StringComparison.Ordinal))
        {
            string hash = uri.AbsolutePath.Split('/')[^1];
            object[] entries = hash == RootTree
                ? Enumerable.Range(1, 20).Select(index => (object)new { path = "Module" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), type = "tree", mode = "040000", sha = index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture) }).ToArray()
                : Enumerable.Range(0, 300).Select(index => (object)new { path = "Source" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".cs", type = "blob", mode = "100644", sha = new string('a', 40), size = 80 }).ToArray();
            payload = new { sha = hash, tree = entries, truncated = false };
        }
        else { payload = new { @private = false, default_branch = "main", license = new { spdx_id = "MIT" } }; }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") });
    }
}

using System.Net;
using System.Security.Cryptography;
using Laura.Core.Models;

namespace Laura.Core.Tests.Models;

public sealed class LocalModelManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-model-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] ArtifactBytes = "GGUFtest-artifact"u8.ToArray();
    private static ModelArtifact Artifact => new("test", "Test", new Uri("https://huggingface.co/test/resolve/pinned/test.gguf"),
        ArtifactBytes.Length, Convert.ToHexString(SHA256.HashData(ArtifactBytes)), "Q4", 2048, 1024, "MIT", new Uri("https://example.com/license"));

    [Fact]
    public async Task InstallsOnlyAfterVerifiedHashAndSize()
    {
        using var manager = CreateManager(new DownloadHandler(ArtifactBytes));
        await manager.InstallAsync(Artifact, null, CancellationToken.None);
        Assert.Equal(["test"], manager.GetInstalledModels());
        Assert.Equal(ArtifactBytes, await File.ReadAllBytesAsync(manager.GetModelPath("test")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("a/b")]
    public void RejectsUnsafeIdentifiers(string id)
    {
        using var manager = CreateManager(new DownloadHandler(ArtifactBytes));
        Assert.Throws<ArgumentException>(() => manager.GetModelPath(id));
    }

    [Fact]
    public async Task CorruptDownloadNeverBecomesInstalled()
    {
        using var manager = CreateManager(new DownloadHandler("GGUFcorrupt-bytes"u8.ToArray()));
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync(Artifact, null, CancellationToken.None));
        Assert.Empty(manager.GetInstalledModels());
    }

    [Fact]
    public async Task ResumesPartialAndChecksContentRange()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "test.gguf.partial"), ArtifactBytes[..5]);
        var handler = new DownloadHandler(ArtifactBytes) { Resume = true };
        using var manager = CreateManager(handler);
        await manager.InstallAsync(Artifact, null, CancellationToken.None);
        Assert.Equal(5, handler.RangeStart);
        Assert.Single(manager.GetInstalledModels());
        manager.Delete("test");
        Assert.Empty(manager.GetInstalledModels());
    }

    [Fact]
    public async Task RefusesUnreviewedDownloadSource()
    {
        using var manager = CreateManager(new DownloadHandler(ArtifactBytes));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.InstallAsync(Artifact with
            { Source = new Uri("https://evil.example/model") }, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledOperationDoesNotPublishArtifact()
    {
        using var manager = CreateManager(new DownloadHandler(ArtifactBytes));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InstallAsync(Artifact, null, cancellation.Token));
        Assert.Empty(manager.GetInstalledModels());
    }

    private LocalModelManager CreateManager(DownloadHandler handler) => new(_directory, new HttpClient(handler), [Artifact]);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }

    private sealed class DownloadHandler(byte[] bytes) : HttpMessageHandler
    {
        internal bool Resume { get; init; }
        internal long? RangeStart { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RangeStart = request.Headers.Range?.Ranges.Single().From;
            int offset = Resume ? (int)(RangeStart ?? 0) : 0;
            var response = new HttpResponseMessage(Resume ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new ByteArrayContent(bytes[offset..]), RequestMessage = request };
            if (Resume) { response.Content.Headers.ContentRange = new(offset, bytes.Length - 1, bytes.Length); }
            return Task.FromResult(response);
        }
    }
}

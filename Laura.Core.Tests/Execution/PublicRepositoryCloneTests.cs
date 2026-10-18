using Laura.Core.Execution;

namespace Laura.Core.Tests.Execution;

/// <summary>Verifies the automatic clone boundary against option, transport, environment and resource substitutions.</summary>
public sealed class PublicRepositoryCloneTests
{
    /// <summary>Rejects commands whose effective transport or configuration could differ from the bounded public clone policy.</summary>
    [Fact]
    public void ExactEffectRejectsChangedOptionsEnvironmentAndDestination()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "clone-fixture"));
        var request = PublicRepositoryClone.Prepare(Path.Combine(root, "git.exe"), "https://github.com/morluto/rea", Path.Combine(root, "new"), Guid.NewGuid());
        Assert.True(PublicRepositoryClone.TryGetTarget(request, out string target)); Assert.Equal(Path.Combine(root, "new"), target);
        Assert.False(PublicRepositoryClone.TryGetTarget(request with { Arguments = [.. request.Arguments, "--upload-pack=unreviewed"] }, out _));
        Assert.False(PublicRepositoryClone.TryGetTarget(request with { EnvironmentVariables = new Dictionary<string, string>() }, out _));
        Assert.False(PublicRepositoryClone.TryGetTarget(request with { WorkingDirectory = Path.Combine(root, "other") }, out _));
        Assert.False(PublicRepositoryClone.TryGetTarget(request with { Background = true }, out _));
    }

    /// <summary>Prevents credentials, local execution transports and arbitrary destinations from inheriting anonymous clone authorization.</summary>
    [Theory]
    [InlineData("file:///private/repository")]
    [InlineData("ext::arbitrary-command")]
    [InlineData("https://owner:token@github.com/morluto/rea")]
    [InlineData("https://github.com/morluto/rea?option=value")]
    [InlineData("https://github.com/morluto/rea/../../other")]
    [InlineData("https://example.invalid/morluto/rea")]
    public void UnsupportedSourcesRequireDifferentExplicitEffects(string url)
        => Assert.Throws<ArgumentException>(() => PublicRepositoryClone.Prepare(Path.GetFullPath("git.exe"), url, Path.GetFullPath("new"), Guid.NewGuid()));
}

using Laura.Core.Execution;

namespace Laura.Core.Tests.Permissions;

/// <summary>Checks immutable literal process parameters without running platform code in domain tests.</summary>
public sealed class CommandRequestTests
{
    [Fact]
    public void ArgumentsAreFrozenAndPreserveUnicodeSpacesAndShellCharactersAsData()
    {
        string[] source = ["a b", "ação 漢字", "$(Remove-Item *)", "\"quoted\""];
        var request = new CommandExecutionRequest(Path.Combine(Path.GetTempPath(), "tool.exe"), source, Path.GetTempPath()) { TimeoutSeconds = int.MaxValue, OutputCharacters = int.MaxValue }.Validated();
        source[0] = "replaced";
        Assert.Equal("a b", request.Arguments[0]); Assert.Equal("ação 漢字", request.Arguments[1]); Assert.Equal("$(Remove-Item *)", request.Arguments[2]);
        Assert.Equal(1800, request.TimeoutSeconds); Assert.Equal(128_000, request.OutputCharacters);
    }

    [Theory]
    [InlineData("unknown.exe", "valid")]
    [InlineData("C:/tool.exe", "bad\0argument")]
    public void UnresolvedOrMalformedCommandsAreRejectedBeforePermissionReview(string executable, string argument)
    {
        Assert.Throws<ArgumentException>(() => new CommandExecutionRequest(executable, [argument], Path.GetTempPath()).Validated());
    }

    [Fact]
    public void ExitCodeAndStatusBothMatterForSuccess()
    {
        Assert.True(new CommandExecutionResult(1, 0, "", "", "exited", false, TimeSpan.Zero).Succeeded);
        Assert.False(new CommandExecutionResult(1, 1, "", "error", "exited", false, TimeSpan.Zero).Succeeded);
        Assert.False(new CommandExecutionResult(1, 0, "", "", "timeout", false, TimeSpan.Zero).Succeeded);
        Assert.False(new CommandExecutionResult(1, null, "", "", "cancelled", false, TimeSpan.Zero).Succeeded);
    }
}

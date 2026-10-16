using Laura.Core.Execution;

namespace Laura.Core.Tests.Execution;

/// <summary>Checks explicit host boundaries without executing PowerShell or interpreting code as authorization.</summary>
public sealed class PowerShellInvocationTests
{
    /// <summary>Preserves literal script parameters while putting all host safety flags before -File.</summary>
    [Fact]
    public void FileModePreservesLiteralUnicodeSpaceAndHostLookingScriptArguments()
    {
        string executable = Path.GetFullPath("pwsh.exe"), root = Path.GetFullPath("Project ação"); string script = Path.Combine(root, "Entry script.ps1");
        var invocation = PowerShellInvocation.Prepare(new(executable, ["-NoProfile", "-File", script, "a b 漢字", "-EncodedCommand", "$(Remove-Item *)"], root));
        Assert.Equal(script, invocation.ScriptPath);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "a b 漢字", "-EncodedCommand", "$(Remove-Item *)"], invocation.Request.Arguments);
    }

    /// <summary>Rejects encoded/ambiguous/interactive host modes and execution-policy overrides before approval or process dispatch.</summary>
    [Theory]
    [InlineData("-EncodedCommand", "YQ==")]
    [InlineData("-e", "YQ==")]
    [InlineData("-ExecutionPolicy", "Bypass", "-Command", "Get-Date")]
    [InlineData("-NoExit", "-Command", "Get-Date")]
    [InlineData("-Profile", "-Command", "Get-Date")]
    [InlineData("-ConfigurationName", "Unknown")]
    [InlineData("-Command", "-")]
    [InlineData("-Command", "Get-Date", "extra text")]
    [InlineData("-File", "relative.ps1")]
    [InlineData("-File", "-")]
    public void UnsupportedHostModesCannotBecomeAReviewedInvocation(params string[] arguments)
    { Assert.Throws<ArgumentException>(() => PowerShellInvocation.Prepare(new(Path.GetFullPath("powershell.exe"), arguments, Path.GetFullPath("Project")))); }

    /// <summary>Does not apply PowerShell host rewriting to unrelated executables and preserves one explicit command string.</summary>
    [Fact]
    public void CommandTextIsImmutableAndOtherExecutablesRetainTheirLiteralArguments()
    {
        var command = PowerShellInvocation.Prepare(new(Path.GetFullPath("pwsh.exe"), ["-Command", "Write-Output 'a b'"], Path.GetFullPath("Project")));
        Assert.Equal("Write-Output 'a b'", command.Request.Arguments[^1]); Assert.Null(command.ScriptPath);
        var other = PowerShellInvocation.Prepare(new(Path.GetFullPath("dotnet.exe"), ["build", "Project with spaces.csproj"], Path.GetFullPath("Project")));
        Assert.Equal(["build", "Project with spaces.csproj"], other.Request.Arguments); Assert.Null(other.ScriptPath);
    }
}

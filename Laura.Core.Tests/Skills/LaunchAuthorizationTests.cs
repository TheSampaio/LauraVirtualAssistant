using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Laura.Core.Skills.Builtin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Skills;

public sealed class LaunchAuthorizationTests
{
    [Fact]
    public async Task ExplicitDenialDoesNotLaunchTheTerminalFallbackAndReportsTheActualBoundary()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        var launcher = new Launcher(true); var skill = new LaunchApplicationSkill(localizer, launcher);
        SkillResponse response = await skill.ExecuteAsync(SkillRequest.Create("open terminal", CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Text), CancellationToken.None);
        Assert.Equal("wt.exe", Assert.Single(launcher.Attempts)); Assert.Contains("authorization", response.SpokenText!, StringComparison.Ordinal);
    }
    [Fact]
    public async Task MissingPrimaryApplicationStillUsesTheExistingExplicitFallback()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        var launcher = new Launcher(false); var skill = new LaunchApplicationSkill(localizer, launcher);
        await skill.ExecuteAsync(SkillRequest.Create("open terminal", CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Text), CancellationToken.None);
        Assert.Equal(new[] { "wt.exe", "powershell.exe" }, launcher.Attempts);
    }
    private sealed class Launcher(bool deny) : IProcessLauncher
    {
        public List<string> Attempts { get; } = [];
        public bool TryOpen(Uri uri) => false;
        public bool TryStart(string fileName, string? arguments = null) => false;
        public Task<ProcessLaunchResult> TryStartObservedAsync(string fileName, string? arguments = null, string? expectedHash = null, CancellationToken cancellationToken = default)
        {
            Attempts.Add(fileName);
            return Task.FromResult(new ProcessLaunchResult(fileName == "powershell.exe", fileName, Reason: deny ? "Authorization denied." : "Executable unavailable.") { StopFallback = deny });
        }
    }
}

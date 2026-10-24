using Laura.Core.Brain;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    /// <summary>Humanizer prose guidance requires the exact enabled hash and becomes unavailable after an external content change.</summary>
    [Fact]
    public async Task HumanizerRequiresExactNativeReviewAndDoesNotScanForGreetings()
    {
        BrainService brain = Create(); await brain.InitializeAsync(); var skills = new BrainSkills(brain, _settings);
        var imported = await skills.ImportAsync("---\nname: Humanizer\ndescription: Clear conversational prose.\n---\nUse clear, natural sentences. Preserve literal code.", "Humanizer");
        Assert.Empty(skills.PresentationGuidance());
        var enabled = await skills.SetEnabledAsync(imported, true);
        var context = new BrainContextBuilder(brain, _settings, skills);
        Assert.Contains("natural sentences", (await context.BuildAsync("hello")).PresentationGuidance, StringComparison.Ordinal);
        string path = Path.Combine(brain.Location, enabled.Path);
        await File.AppendAllTextAsync(path, "\nChanged external guidance."); await brain.RefreshAsync();
        Assert.Empty(skills.PresentationGuidance());
    }
}

using Laura.Core.Configuration;

namespace Laura.Core.Tests.Configuration;

/// <summary>Prevents a diagnostic flag from silently using real credentials or normal settings.</summary>
public sealed class DiagnosticStoragePolicyTests
{
    /// <summary>Missing storage and owner/ancestor/child locations must fail before service construction.</summary>
    [Fact]
    public void DiagnosticsCannotUseOwnerStorageOrImplicitDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "DiagnosticPolicy-" + Guid.NewGuid().ToString("N"));
        string owner = Path.Combine(root, "Owner");
        foreach (string? path in new[] { null, "", root, owner, Path.Combine(owner, "Child"), Path.Combine(owner, "Child", "..") })
        { Assert.Throws<InvalidOperationException>(() => DiagnosticStoragePolicy.RequireIsolated(path, owner)); }
        Assert.Equal(Path.Combine(root, "Fixture"), DiagnosticStoragePolicy.RequireIsolated(Path.Combine(root, "Fixture"), owner));
    }
}

namespace Laura.Core.Abstractions;

/// <summary>Native-only installation action for the optional credential isolation boundary; never exposed as an assistant tool.</summary>
public interface IChatGptBrokerSetup
{
    /// <summary>Installs the reviewed packaged Windows service through the operating system administrator-consent dialog.</summary>
    Task InstallAsync(CancellationToken cancellationToken = default);
}

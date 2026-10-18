namespace Laura.Core.Abstractions;

/// <summary>Resolves real operating-system known folders, including redirected owner locations.</summary>
public interface IKnownFolderResolver
{
    /// <summary>Resolves an exact supported alias, or returns null when the value is not an alias.</summary>
    string? Resolve(string alias);
}

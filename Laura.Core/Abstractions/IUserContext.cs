namespace Laura.Core.Abstractions;

/// <summary>
/// Information about the user Laura serves.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Gets the name Laura uses to address the user.
    ///
    /// This is the nickname the user set in the settings, or the operating-system
    /// account name when no nickname has been chosen.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the name derived from the operating-system account, used as the default
    /// when the user has not set a nickname.
    /// </summary>
    string AccountName { get; }
}

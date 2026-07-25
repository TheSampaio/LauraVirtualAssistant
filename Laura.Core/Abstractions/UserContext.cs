using System.Globalization;

namespace Laura.Core.Abstractions;

/// <summary>
/// Implementation of <see cref="IUserContext"/> that prefers the user-chosen
/// nickname and falls back to the operating-system account name.
/// </summary>
public sealed class UserContext : IUserContext
{
    private readonly ISettingsService _settings;

    /// <summary>
    /// Initializes the context, deriving the account name from the OS account.
    ///
    /// Args:
    ///     settings: Source of the nickname the user may have chosen.
    /// </summary>
    public UserContext(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        AccountName = FormatUserName(Environment.UserName);
    }

    /// <inheritdoc />
    public string AccountName { get; }

    /// <inheritdoc />
    public string DisplayName
    {
        get
        {
            string nickname = _settings.Current.UserNickname;
            return string.IsNullOrWhiteSpace(nickname) ? AccountName : nickname.Trim();
        }
    }

    /// <summary>
    /// Turns the account name into something natural to hear.
    ///
    /// Accounts are often stored in lowercase or with dots and underscores; saying
    /// "kellvyn.sampaio" would sound mechanical.
    ///
    /// Args:
    ///     userName: The operating-system account name.
    ///
    /// Returns:
    ///     The first name with an initial capital, or an empty string when the
    ///     account has no usable name.
    /// </summary>
    private static string FormatUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return string.Empty;
        }

        string firstName = userName
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(string.Empty);

        return firstName.Length == 0
            ? string.Empty
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(firstName.ToLowerInvariant());
    }
}

using Microsoft.Extensions.Logging;

namespace Laura.App.Views;

public sealed partial class SettingsForm
{
    /// <summary>Keeps technical diagnostics in logs and presents actionable localized errors.</summary>
    private string DescribeFailure(Exception exception)
    {
        _logger.LogWarning(exception, "Settings operation failed.");
        string key = exception switch
        {
            HttpRequestException => "ui.errors.network",
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => "ui.errors.native",
            IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException => "ui.errors.storage",
            ArgumentException => "ui.errors.invalid",
            _ => "ui.errors.unexpected",
        };
        return Text_(key);
    }
}

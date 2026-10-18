using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Diagnostics;

/// <summary>Logs codes and exception frame identities only; formatted messages and structured argument values are deliberately ignored.</summary>
public sealed class RuntimeLogProvider(RuntimeJournal journal) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(journal, categoryName.Split('.').Last());
    /// <inheritdoc />
    public void Dispose() { }
    private sealed class Logger(RuntimeJournal journal, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= journal.MinimumLevel && level != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }
            string template = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string ?? "" : "";
            string code = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..8];
            string known = template switch
            {
                "Could not persist conversation message." => "transcript.persistence_failed",
                "The assistant processing loop ended with a failure." => "engine.worker_failed",
                "Failed to handle {Message}." => "engine.request_failed",
                "Settings loaded (culture {Culture})." => "settings.loaded",
                "Settings updated (culture {Culture})." => "settings.updated",
                "Failed to read {Path}; using the defaults." => "settings.read_failed",
                "Persistent memory unavailable; continuing with session history." => "memory.unavailable",
                "Could not fetch the weather from wttr.in." => "weather.unavailable",
                "Assistant engine started." => "engine.started",
                "Assistant engine stopped." => "engine.stopped",
                _ => category + "." + code
            };
            journal.Record("log." + logLevel + "." + known, amount: eventId.Id, outcome: exception?.GetType().Name ?? "event", interactionId: LauraDiagnostics.CurrentInteractionId);
            if (exception is not null)
            {
                foreach (var frame in new StackTrace(exception, false).GetFrames().Take(8))
                { var method = frame.GetMethod(); journal.Record("exception." + method?.DeclaringType?.Name + "." + method?.Name, outcome: exception.GetType().Name, interactionId: LauraDiagnostics.CurrentInteractionId); }
            }
        }
    }
}

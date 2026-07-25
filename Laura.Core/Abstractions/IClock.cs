namespace Laura.Core.Abstractions;

/// <summary>
/// Time source for the application.
///
/// Exists so rules that depend on date and time - greetings by time of
/// day, full-hour announcements - can be tested without depending on the
/// machine's real clock.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current instant in the local time zone.
    /// </summary>
    DateTimeOffset Now { get; }
}

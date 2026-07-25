namespace Laura.Core.Abstractions;

/// <summary>
/// Implementation of <see cref="IClock"/> backed by the operating system clock.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}

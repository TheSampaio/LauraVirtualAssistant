namespace Laura.Core.Abstractions;

/// <summary>
/// Implementação de <see cref="IClock"/> apoiada no relógio do sistema operacional.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}

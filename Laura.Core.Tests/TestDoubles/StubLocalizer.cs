using System.Globalization;
using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test localizer that returns the key itself, avoiding language files.
/// </summary>
public sealed class StubLocalizer : ILocalizer
{
    /// <inheritdoc />
    public event EventHandler<CultureInfo>? CultureChanged;

    /// <inheritdoc />
    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <inheritdoc />
    public IReadOnlyList<CultureInfo> AvailableCultures => [Culture];

    /// <inheritdoc />
    public void SetCulture(CultureInfo culture) => CultureChanged?.Invoke(this, culture);

    /// <inheritdoc />
    public string Get(string key, params object?[] arguments) => key;

    /// <inheritdoc />
    public IReadOnlyList<string> GetPhrases(string key) => [];
}

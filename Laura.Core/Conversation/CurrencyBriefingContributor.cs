using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Conversation;

/// <summary>Reads the latest published USD/BRL sale reference from Banco Central's SGS series 1.</summary>
public sealed class CurrencyBriefingContributor(HttpClient http, IClock clock, ILocalizer localizer) : IBriefingContributor, IDisposable
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private BriefingSection? _cached;
    private DateTimeOffset _cachedAt;
    private string _cacheCulture = "";

    /// <summary>Releases the cache gate owned by this contributor.</summary>
    public void Dispose() => _cacheGate.Dispose();

    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && _cacheCulture == localizer.Culture.Name && clock.Now >= _cachedAt && clock.Now - _cachedAt < TimeSpan.FromHours(1)) { return _cached; }
            using HttpResponseMessage response = await http.GetAsync(new Uri("https://api.bcb.gov.br/dados/serie/bcdata.sgs.1/dados/ultimos/1?formato=json"), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) { return null; }
            JsonElement quote = json.RootElement[0];
            if (quote.ValueKind != JsonValueKind.Object || !quote.TryGetProperty("valor", out JsonElement rate) || !quote.TryGetProperty("data", out JsonElement published)
                || rate.ValueKind != JsonValueKind.String || published.ValueKind != JsonValueKind.String
                || !decimal.TryParse(rate.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) || value <= 0
                || !DateOnly.TryParseExact(published.GetString(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) { return null; }
            if (date > DateOnly.FromDateTime(clock.Now.DateTime) || date < DateOnly.FromDateTime(clock.Now.DateTime.AddDays(-7))) { return null; }
            string formatted = "R$ " + Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("N2", BrazilianCulture);
            string spoken = localizer.Get(LocalizationKeys.Greeting.CurrencyReference, date.ToString("d", localizer.Culture), formatted);
            _cached = new("USD/BRL", "Banco Central do Brasil · SGS 1", clock.Now,
                new { SaleReference = value, FormattedSaleReference = formatted, PublishedDate = date, SpokenSummary = spoken, Kind = "Latest published sale reference; not a real-time market quote" });
            _cachedAt = clock.Now;
            _cacheCulture = localizer.Culture.Name;
            return _cached;
        }
        finally { _cacheGate.Release(); }
    }
}

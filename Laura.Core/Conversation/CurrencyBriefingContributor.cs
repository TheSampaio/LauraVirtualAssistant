using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Reads the latest published USD/BRL sale reference from Banco Central's SGS series 1.</summary>
public sealed class CurrencyBriefingContributor(HttpClient http, IClock clock) : IBriefingContributor
{
    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri("https://api.bcb.gov.br/dados/serie/bcdata.sgs.1/dados/ultimos/1?formato=json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) { return null; }
        JsonElement quote = json.RootElement[0];
        if (!quote.TryGetProperty("valor", out JsonElement rate) || !quote.TryGetProperty("data", out JsonElement published)
            || !decimal.TryParse(rate.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || value <= 0
            || !DateOnly.TryParseExact(published.GetString(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) { return null; }
        return new("USD/BRL", "Banco Central do Brasil · SGS 1", clock.Now,
            new { SaleReference = value, PublishedDate = date, Kind = "Latest published reference rate, not a real-time market quote" });
    }
}

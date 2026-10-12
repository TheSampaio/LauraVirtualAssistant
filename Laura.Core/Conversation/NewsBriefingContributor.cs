using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Collects recent BBC headlines with original publication dates and article links.</summary>
public sealed class NewsBriefingContributor(HttpClient http, ISettingsService settings, IClock clock) : IBriefingContributor
{
    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        Uri feed = new(settings.Current.Culture == "pt-BR" ? "https://feeds.bbci.co.uk/portuguese/rss.xml" : "https://feeds.bbci.co.uk/news/world/rss.xml");
        using HttpResponseMessage response = await http.GetAsync(feed, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 });
        XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        var headlines = document.Descendants("item").Select(item => new
        {
            Title = item.Element("title")?.Value.Trim() ?? "",
            Url = item.Element("link")?.Value.Trim() ?? "",
            Published = DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : (DateTimeOffset?)null,
        }).Where(item => item.Title.Length is > 0 and <= 400 && Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? url) && url.Scheme == "https"
            && item.Published is { } date && date <= clock.Now.AddMinutes(5) && date >= clock.Now.AddDays(-3))
            .OrderByDescending(item => item.Published).DistinctBy(item => item.Url).Take(3).ToArray();
        return headlines.Length == 0 ? null : new("News", "BBC", clock.Now, headlines);
    }
}

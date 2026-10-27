using System.Xml.Linq;

namespace Fetchshelf.Clients.Goodreads;

/// <summary>Fetches and parses a Goodreads shelf's RSS feed, Fetchshelf-spec.md §5.4, §7.8.</summary>
public sealed class GoodreadsClient
{
    private readonly HttpClient _httpClient;

    public GoodreadsClient(HttpClient httpClient, GoodreadsClientOptions options)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>The feed channel's title, alongside its parsed items.</summary>
    public async Task<(string ChannelTitle, IReadOnlyList<GoodreadsFeedItem> Items)> FetchShelfAsync(
        string feedUrl, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(feedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        var channel = document.Root?.Element("channel")
            ?? throw new FormatException("Goodreads RSS feed is missing its <channel> element.");

        var channelTitle = channel.Element("title")?.Value ?? string.Empty;
        var items = channel.Elements("item").Select(ParseItem).ToList();
        return (channelTitle, items);
    }

    private static GoodreadsFeedItem ParseItem(XElement item)
    {
        var guid = item.Element("guid")?.Value ?? throw new FormatException("Goodreads feed item is missing a <guid>.");
        var rawTitle = item.Element("title")?.Value ?? string.Empty;
        var rawAuthorName = item.Element("author_name")?.Value;
        var descriptionHtml = item.Element("description")?.Value ?? string.Empty;
        var largeImageUrl = item.Element("book_large_image_url")?.Value;
        var isbn = item.Element("isbn")?.Value;

        // Series suffix is extracted from the raw title before HTML-unescaping, matching the
        // reference's ordering (goodreads.rs run_goodreads_import).
        var (strippedTitle, rawSeriesName, seriesNumber) = GoodreadsTitleParser.ExtractSeries(rawTitle);
        var title = GoodreadsTitleParser.Unescape(strippedTitle);
        var seriesName = rawSeriesName is null ? null : GoodreadsTitleParser.Unescape(rawSeriesName);

        var authors = string.IsNullOrWhiteSpace(rawAuthorName)
            ? GoodreadsDescriptionScraper.ExtractAuthors(descriptionHtml)
            : [GoodreadsTitleParser.Unescape(rawAuthorName.Replace('.', ' '))];

        var coverUrl = string.IsNullOrWhiteSpace(largeImageUrl)
            ? GoodreadsDescriptionScraper.ExtractCoverUrl(descriptionHtml)
            : largeImageUrl;

        return new GoodreadsFeedItem
        {
            Guid = guid,
            Title = title,
            Authors = authors,
            SeriesName = seriesName,
            SeriesNumber = seriesNumber,
            CoverUrl = coverUrl,
            BookUrl = GoodreadsDescriptionScraper.ExtractBookUrl(descriptionHtml),
            Isbn = string.IsNullOrWhiteSpace(isbn) ? null : isbn,
        };
    }
}

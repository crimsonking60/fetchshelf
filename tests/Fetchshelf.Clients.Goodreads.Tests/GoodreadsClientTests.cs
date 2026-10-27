using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Goodreads.Tests;

public sealed class GoodreadsClientTests
{
    private static GoodreadsClient CreateClient(string responseXml)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseXml, Encoding.UTF8, "application/rss+xml"),
        });
        return new GoodreadsClient(new HttpClient(handler), new GoodreadsClientOptions());
    }

    private const string FullFieldsFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>My bookshelf: to-read</title>
            <item>
              <guid>https://www.goodreads.com/review/show/1-mistborn</guid>
              <book_id>68428</book_id>
              <title>Mistborn (The Mistborn Saga, #1)</title>
              <author_name>Brandon Sanderson</author_name>
              <isbn>9780765311788</isbn>
              <book_large_image_url>https://images.example.com/mistborn.jpg</book_large_image_url>
              <description>&lt;p&gt;A great fantasy novel.&lt;/p&gt;</description>
            </item>
          </channel>
        </rss>
        """;

    private const string FallbackScrapeFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>Group shelf: to-read</title>
            <item>
              <guid>https://www.goodreads.com/review/show/2-good-omens</guid>
              <title>Good Omens</title>
              <isbn></isbn>
              <description>&lt;div class="cover"&gt;&lt;img src="https://images.example.com/good-omens.jpg"/&gt;&lt;/div&gt;&lt;a href="/book/show/12345"&gt;Good Omens&lt;/a&gt;&lt;span itemprop="author"&gt;&lt;span itemprop="name"&gt;Terry Pratchett&lt;/span&gt;&lt;/span&gt;&lt;span itemprop="author"&gt;&lt;span itemprop="name"&gt;Neil Gaiman&lt;/span&gt;&lt;/span&gt;</description>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public async Task FetchShelfAsync_ParsesChannelTitleAndItemFields()
    {
        var client = CreateClient(FullFieldsFeed);

        var (channelTitle, items) = await client.FetchShelfAsync("https://www.goodreads.com/review/list_rss/1?shelf=to-read");

        Assert.Equal("My bookshelf: to-read", channelTitle);
        var item = Assert.Single(items);
        Assert.Equal("https://www.goodreads.com/review/show/1-mistborn", item.Guid);
        Assert.Equal("Mistborn", item.Title);
        Assert.Equal("The Mistborn Saga", item.SeriesName);
        Assert.Equal(1, item.SeriesNumber);
        Assert.Equal(["Brandon Sanderson"], item.Authors);
        Assert.Equal("https://images.example.com/mistborn.jpg", item.CoverUrl);
        Assert.Equal("9780765311788", item.Isbn);
    }

    [Fact]
    public async Task FetchShelfAsync_MissingAuthorAndImageFields_FallsBackToScrapingDescription()
    {
        var client = CreateClient(FallbackScrapeFeed);

        var (_, items) = await client.FetchShelfAsync("https://www.goodreads.com/review/list_rss/1?shelf=to-read");

        var item = Assert.Single(items);
        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], item.Authors);
        Assert.Equal("https://images.example.com/good-omens.jpg", item.CoverUrl);
        Assert.Equal("https://www.goodreads.com/book/show/12345", item.BookUrl);
    }
}

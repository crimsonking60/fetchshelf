namespace Fetchshelf.Clients.Goodreads.Tests;

public class GoodreadsDescriptionScraperTests
{
    [Fact]
    public void ExtractCoverUrl_CoverImagePresent_ReturnsSrc()
    {
        var html = """<div class="cover"><img src="https://images.example.com/cover.jpg"/></div>""";

        Assert.Equal("https://images.example.com/cover.jpg", GoodreadsDescriptionScraper.ExtractCoverUrl(html));
    }

    [Fact]
    public void ExtractCoverUrl_NoCover_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GoodreadsDescriptionScraper.ExtractCoverUrl("<p>no cover here</p>"));
    }

    [Fact]
    public void ExtractBookUrl_AbsoluteUserShelfLink_ReturnsHref()
    {
        var html = """<a href="https://www.goodreads.com/book/show/68428.Mistborn">Mistborn</a>""";

        Assert.Equal("https://www.goodreads.com/book/show/68428.Mistborn", GoodreadsDescriptionScraper.ExtractBookUrl(html));
    }

    [Fact]
    public void ExtractBookUrl_RelativeGroupShelfLink_ResolvesToAbsolute()
    {
        var html = """<a href="/book/show/68428.Mistborn">Mistborn</a>""";

        Assert.Equal("https://www.goodreads.com/book/show/68428.Mistborn", GoodreadsDescriptionScraper.ExtractBookUrl(html));
    }

    [Fact]
    public void ExtractBookUrl_NoLink_ReturnsNull()
    {
        Assert.Null(GoodreadsDescriptionScraper.ExtractBookUrl("<p>no link here</p>"));
    }

    [Fact]
    public void ExtractAuthors_MultipleAuthors_ReturnsEachName()
    {
        var html = """
            <span itemprop="author"><span itemprop="name">Terry Pratchett</span></span>
            <span itemprop="author"><span itemprop="name">Neil Gaiman</span></span>
            """;

        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], GoodreadsDescriptionScraper.ExtractAuthors(html));
    }

    [Fact]
    public void ExtractAuthors_NoAuthorMarkup_ReturnsEmpty()
    {
        Assert.Empty(GoodreadsDescriptionScraper.ExtractAuthors("<p>no author markup</p>"));
    }
}

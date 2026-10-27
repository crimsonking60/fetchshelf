namespace Fetchshelf.Clients.Goodreads.Tests;

public class GoodreadsListIdTests
{
    [Fact]
    public void Derive_SameUrl_YieldsSameId()
    {
        const string url = "https://www.goodreads.com/review/list_rss/114580095?shelf=to-read";

        var first = GoodreadsListId.Derive(url);
        var second = GoodreadsListId.Derive(url);

        Assert.Equal(first, second);
        Assert.Equal("114580095:to-read", first);
    }

    [Fact]
    public void Derive_DifferentShelf_YieldsDifferentId()
    {
        var toRead = GoodreadsListId.Derive("https://www.goodreads.com/review/list_rss/114580095?shelf=to-read");
        var read = GoodreadsListId.Derive("https://www.goodreads.com/review/list_rss/114580095?shelf=read");

        Assert.NotEqual(toRead, read);
    }

    [Fact]
    public void Derive_MissingShelfParameter_Throws()
    {
        Assert.Throws<FormatException>(() => GoodreadsListId.Derive("https://www.goodreads.com/review/list_rss/114580095"));
    }
}

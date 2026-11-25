namespace Fetchshelf.Clients.Hardcover.Tests;

public class HardcoverListReferenceTests
{
    [Theory]
    [InlineData("books-to-fetch", null, "books-to-fetch")]
    [InlineData(" books-to-fetch ", null, "books-to-fetch")]
    [InlineData("@someone/books-to-fetch", "someone", "books-to-fetch")]
    [InlineData("@someone/lists/books-to-fetch", "someone", "books-to-fetch")]
    [InlineData("https://hardcover.app/@someone/lists/books-to-fetch", "someone", "books-to-fetch")]
    [InlineData("http://www.hardcover.app/@some_one/lists/books-to-fetch/", "some_one", "books-to-fetch")]
    [InlineData("hardcover.app/@someone/lists/books-to-fetch?page=2", "someone", "books-to-fetch")]
    [InlineData("HTTPS://Hardcover.app/@Someone/lists/books-to-fetch#top", "Someone", "books-to-fetch")]
    public void TryParse_ReadsTheSlugAndOwner(string text, string? owner, string slug)
    {
        Assert.True(HardcoverListReference.TryParse(text, out var reference));
        Assert.Equal(new HardcoverListReference(owner, slug), reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("books to fetch")]
    [InlineData("@someone")]
    [InlineData("someone/books-to-fetch")]
    [InlineData("https://example.com/@someone/lists/books-to-fetch")]
    [InlineData("https://hardcover.app/books/dune")]
    [InlineData("https://hardcover.app/@someone/lists")]
    public void TryParse_NoList_IsFalse(string text) => Assert.False(HardcoverListReference.TryParse(text, out _));

    [Fact]
    public void ToString_IsTheSlug_OrOwnerAndSlug()
    {
        Assert.Equal("books-to-fetch", new HardcoverListReference(null, "books-to-fetch").ToString());
        Assert.Equal("@someone/books-to-fetch", new HardcoverListReference("someone", "books-to-fetch").ToString());
    }
}

using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class SearchResultViewModelTests
{
    [Fact]
    public void ShortenAuthors_FitsInTheLimit_IsUnchanged() =>
        Assert.Equal("Brandon Sanderson", SearchResultViewModel.ShortenAuthors("Brandon Sanderson"));

    [Fact]
    public void ShortenAuthors_ExactlyAtTheLimit_IsUnchanged()
    {
        var forty = new string('a', 40);

        Assert.Equal(forty, SearchResultViewModel.ShortenAuthors(forty));
    }

    [Fact]
    public void ShortenAuthors_TooLong_KeepsWholeAuthorsThatFit()
    {
        // "Alpha Author, Beta Author, Gamma Author" is 39 characters; adding ", Delta Author" would pass 40.
        var result = SearchResultViewModel.ShortenAuthors("Alpha Author, Beta Author, Gamma Author, Delta Author, Epsilon Author");

        Assert.Equal("Alpha Author, Beta Author, Gamma Author, ... more", result);
    }

    [Fact]
    public void ShortenAuthors_AuthorListEndingExactlyAtTheLimit_KeepsThatAuthor()
    {
        // The first two authors are exactly 40 characters together.
        var result = SearchResultViewModel.ShortenAuthors("Alpha Author One, Beta Author Two Name, Gamma");

        Assert.Equal("Alpha Author One, Beta Author Two Name, ... more", result);
    }

    [Fact]
    public void ShortenAuthors_FirstAuthorAloneIsTooLong_CutsMidName()
    {
        const string authors = "Hubert Blaine Wolfeschlegelsteinhausenbergerdorff Sr., Someone Else";

        var result = SearchResultViewModel.ShortenAuthors(authors);

        Assert.Equal(authors[..SearchResultViewModel.MaxAuthorsLength] + "... more", result);
    }

    [Fact]
    public void ShortenAuthors_ResultNeverExceedsTheLimitBeforeTheEnding()
    {
        var many = string.Join(", ", Enumerable.Range(1, 80).Select(i => $"Author Number {i}"));

        var result = SearchResultViewModel.ShortenAuthors(many);

        Assert.EndsWith("... more", result);
        Assert.True(result.Length - ", ... more".Length <= SearchResultViewModel.MaxAuthorsLength);
    }
}

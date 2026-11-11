using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class SearchQueryBuilderTests
{
    [Fact]
    public void Build_Defaults_AskWhatThePageAlwaysAsked()
    {
        var query = SearchQueryBuilder.Build("kings", null, null, null, 1, [Language.English]);

        Assert.Equal("kings", query.Text);
        Assert.Equal([MamSearchField.Title, MamSearchField.Author], query.SearchFields);
        Assert.Equal(MamSearchTarget.New, query.Target);
        Assert.Null(query.Kind);
        Assert.Equal(MamSortOrder.NewestFirst, query.SortOrder);
        Assert.Equal(0, query.PageOffset);
        Assert.Equal(100, query.PerPage);
        Assert.True(query.ReturnDownloadLink);
        Assert.Equal([(int)Language.English], query.LanguageIds);
    }

    [Fact]
    public void Build_NoText_SearchesNoFields()
    {
        var query = SearchQueryBuilder.Build("  ", ["series"], null, null, 1, []);

        Assert.Null(query.Text);
        Assert.Empty(query.SearchFields);
    }

    [Fact]
    public void Build_Fields_InThePagesOrder_UnknownOnesIgnored()
    {
        var query = SearchQueryBuilder.Build("x", ["filenames", "Narrator", "nonsense", "series", "description", "tags"], null, null, 1, []);

        Assert.Equal(
            [MamSearchField.Series, MamSearchField.Narrator, MamSearchField.Description, MamSearchField.Tags, MamSearchField.Filenames],
            query.SearchFields);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void Build_NoKnownField_SearchesTitleAndAuthor(string? field)
    {
        var query = SearchQueryBuilder.Build("x", field is null ? [] : [field], null, null, 1, []);

        Assert.Equal([MamSearchField.Title, MamSearchField.Author], query.SearchFields);
    }

    [Theory]
    [InlineData("relevance", MamSortOrder.NewestFirst)]
    [InlineData("newest", MamSortOrder.NewestByDate)]
    [InlineData("oldest", MamSortOrder.OldestFirst)]
    [InlineData("seeders", MamSortOrder.MostSeeders)]
    [InlineData("SNATCHED", MamSortOrder.MostSnatches)]
    [InlineData("size", MamSortOrder.LargestFirst)]
    [InlineData("title", MamSortOrder.TitleAsc)]
    [InlineData("nonsense", MamSortOrder.NewestFirst)]
    public void Build_Sort(string sort, MamSortOrder expected) =>
        Assert.Equal(expected, SearchQueryBuilder.Build("x", null, sort, null, 1, []).SortOrder);

    [Theory]
    [InlineData("all", false, null)]
    [InlineData("freeleech", false, "Freeleech")]
    [InlineData("free_or_vip", false, "FreeOrVip")]
    [InlineData("vip", false, "Vip")]
    [InlineData("bookmarks", true, null)]
    [InlineData("not_snatched", false, null)]
    [InlineData("nonsense", false, null)]
    public void Build_Type(string type, bool bookmarks, string? kind)
    {
        var query = SearchQueryBuilder.Build("x", null, null, type, 1, []);

        Assert.Equal(bookmarks ? MamSearchTarget.Bookmarks : MamSearchTarget.New, query.Target);
        Assert.Equal(kind, query.Kind?.ToString());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 200)]
    [InlineData(0, 0)]
    [InlineData(-4, 0)]
    public void Build_Page_SetsTheOffset(int page, long offset) =>
        Assert.Equal(offset, SearchQueryBuilder.Build("x", null, null, null, page, []).PageOffset);
}

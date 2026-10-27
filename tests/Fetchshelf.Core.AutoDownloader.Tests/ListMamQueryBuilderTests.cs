namespace Fetchshelf.Core.AutoDownloader.Tests;

public class ListMamQueryBuilderTests
{
    [Fact]
    public void BuildTitleQuery_ColonInTitle_AddsAlternateBeforeColon()
    {
        var query = ListMamQueryBuilder.BuildTitleQuery("Dune: Part Two");

        Assert.Equal("(\"Dune\"|\"Dune: Part Two\")", query);
    }

    [Fact]
    public void BuildTitleQuery_QuotesAndSlashesReplacedWithSpace()
    {
        var query = ListMamQueryBuilder.BuildTitleQuery("Book's Title/Subtitle");

        Assert.Equal("Book s Title Subtitle", query);
    }

    [Fact]
    public void BuildTitleQuery_BracketedSuffixStripped()
    {
        var query = ListMamQueryBuilder.BuildTitleQuery("Mistborn (The Final Empire)");

        Assert.Equal("Mistborn ", query);
    }

    [Fact]
    public void BuildTitleQuery_AndExpandedToAlternation()
    {
        var query = ListMamQueryBuilder.BuildTitleQuery("Pride and Prejudice");

        Assert.Equal("Pride (and|&) Prejudice", query);
    }

    [Fact]
    public void BuildTitleQuery_AmpersandExpandedToAlternation()
    {
        var query = ListMamQueryBuilder.BuildTitleQuery("Will & Grace");

        Assert.Equal("Will (and|&) Grace", query);
    }

    [Fact]
    public void Build_CombinesTitleAndAuthors()
    {
        var query = ListMamQueryBuilder.Build("Mistborn", ["Brandon Sanderson"]);

        Assert.Equal("@title Mistborn @author (\"Brandon Sanderson\")", query);
    }

    [Fact]
    public void Build_MultipleAuthors_JoinedWithPipe()
    {
        var query = ListMamQueryBuilder.Build("Good Omens", ["Terry Pratchett", "Neil Gaiman"]);

        Assert.Equal("@title Good Omens @author (\"Terry Pratchett\"|\"Neil Gaiman\")", query);
    }
}

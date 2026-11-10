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

    [Theory]
    [InlineData("Sarah J. Maas", "Sarah (J|J.) Maas")]
    [InlineData("Sarah J Maas", "Sarah (J|J.) Maas")]
    [InlineData("J. K. Rowling", "(J|J.) (K|K.) Rowling")]
    [InlineData("Iain M. Banks", "Iain (M|M.) Banks")]
    [InlineData("Brandon Sanderson", "Brandon Sanderson")]
    [InlineData("J.R.R. Tolkien", "J.R.R. Tolkien")]
    [InlineData("Ursula K. Le Guin", "Ursula (K|K.) Le Guin")]
    public void BuildAuthorQuery_InitialMatchesWithOrWithoutFullStop(string author, string expected)
    {
        Assert.Equal(expected, ListMamQueryBuilder.BuildAuthorQuery(author));
    }

    [Fact]
    public void Build_AuthorWithInitial_MatchesTheInitialEitherWay()
    {
        var query = ListMamQueryBuilder.Build("A Court of Thorns and Roses", ["Sarah J. Maas"]);

        Assert.Equal("@title A Court of Thorns (and|&) Roses @author (\"Sarah (J|J.) Maas\")", query);
    }
}

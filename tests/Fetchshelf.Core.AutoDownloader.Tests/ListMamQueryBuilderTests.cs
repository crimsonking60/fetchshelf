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
    public void Build_TitleWithTildes_LeavesNoNotOperator()
    {
        // Two torrents in the live library: MAM answered "Your search is only not" to the tildes.
        var query = ListMamQueryBuilder.Build("Encyclopaedia Eorzea ~The World of Final Fantasy XIV~ Volume I", ["Square Enix"]);

        Assert.Equal("@title Encyclopaedia Eorzea  The World of Final Fantasy XIV  Volume I @author (\"Square Enix\")", query);
    }

    [Theory]
    [InlineData("~Tilde~", " Tilde ")]
    [InlineData("Yes! No!", "Yes  No ")]
    [InlineData("-ish", " ish")]
    [InlineData("Twenty -One", "Twenty  One")]
    [InlineData("Spider-Man", "Spider-Man")]
    [InlineData("The \"Real\" Story", "The  Real  Story")]
    [InlineData("A | B", "A   B")]
    [InlineData("@home", " home")]
    [InlineData("x^2 = y", "x 2   y")]
    [InlineData("<Tag> $5", " Tag   5")]
    [InlineData("Back\\slash", "Back slash")]
    [InlineData("Open (bracket", "Open  bracket")]
    [InlineData("Close] bracket", "Close  bracket")]
    [InlineData("{Curly}", " Curly ")]
    public void BuildTitleQuery_SearchOperators_AreTakenOut(string title, string expected)
    {
        Assert.Equal(expected, ListMamQueryBuilder.BuildTitleQuery(title));
    }

    [Fact]
    public void BuildTitleQuery_StarStillMatchesLiterally()
    {
        Assert.Equal("Catch\"*\"22", ListMamQueryBuilder.BuildTitleQuery("Catch*22"));
    }

    [Fact]
    public void BuildTitleQuery_QuotesInATitleWithAColon_DontBreakThePhrases()
    {
        Assert.Equal("(\"The  Last \"|\"The  Last : Part One\")", ListMamQueryBuilder.BuildTitleQuery("The \"Last\": Part One"));
    }

    [Fact]
    public void Build_QuotesInAnAuthorsName_AreTakenOut()
    {
        Assert.Equal("@title Book @author (\"Dwayne  The Rock  Johnson\")", ListMamQueryBuilder.Build("Book", ["Dwayne \"The Rock\" Johnson"]));
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
    public void Build_NoAuthors_SearchesTheTitleAlone()
    {
        var query = ListMamQueryBuilder.Build("Mistborn", []);

        Assert.Equal("@title Mistborn", query);
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

    [Theory]
    [InlineData("Ender's Saga", "Ender s Saga")]
    [InlineData("Spider-Man", "Spider Man")]
    [InlineData("Who?/What", "Who What")]
    [InlineData("A*B | \"C\" (D) @E", "A B C D E")]
    [InlineData("James S. A. Corey", "James S. A. Corey")]
    [InlineData("Мастер и Маргарита", "Мастер и Маргарита")]
    public void PlainWords_TakesOutSearchSyntax(string name, string expected) =>
        Assert.Equal(expected, ListMamQueryBuilder.PlainWords(name));
}

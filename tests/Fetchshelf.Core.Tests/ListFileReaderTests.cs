using Fetchshelf.Core.Lists;

namespace Fetchshelf.Core.Tests;

public sealed class ListFileReaderTests
{
    private const string GoodreadsHeader =
        "Book Id,Title,Author,Author l-f,Additional Authors,ISBN,ISBN13,My Rating,Average Rating,Publisher,Binding,Number of Pages,"
        + "Year Published,Original Publication Year,Date Read,Date Added,Bookshelves,Bookshelves with positions,Exclusive Shelf,"
        + "My Review,Spoiler,Private Notes,Read Count,Owned Copies";

    private const string GoodreadsExport = GoodreadsHeader + "\n"
        + "234225,\"Dune (Dune, #1)\",Frank Herbert,\"Herbert, Frank\",,\"=\"\"0441172717\"\"\",\"=\"\"9780441172719\"\"\",0,4.27,Ace,Paperback,688,1990,1965,,2026/01/02,to-read,to-read (#1),to-read,,,,0,0\n"
        + "12345,The Talisman,Stephen King,\"King, Stephen\",Peter Straub,\"=\"\"\"\"\",\"=\"\"\"\"\",5,4.1,Viking,Hardcover,646,1984,1984,2025/03/01,2025/02/01,,,read,\"Loved it,\nreally.\",,,1,0\n"
        + "67890,\"Words of Radiance (The Stormlight Archive, #2)\",Brandon Sanderson,\"Sanderson, Brandon\",,\"=\"\"\"\"\",\"=\"\"\"\"\",0,4.76,Tor,Hardcover,1087,2014,2014,,2026/01/05,to-read,to-read (#2),to-read,,,,0,0\n";

    private const string StoryGraphExport =
        "Title,Authors,Contributors,ISBN/UID,Format,Read Status,Date Added,Last Date Read,Dates Read,Read Count,Moods,Pace,"
        + "Character- or Plot-Driven?,Strong Character Development?,Loveable Characters?,Diverse Characters?,Flawed Characters?,"
        + "Star Rating,Review,Content Warnings,Content Warning Description,Tags,Owned?\n"
        + "Good Omens,\"Terry Pratchett, Neil Gaiman\",,9780060853983,paperback,to-read,2026/01/03,,,0,,,,,,,,,,,,,No\n"
        + "The Hobbit,J.R.R. Tolkien,,,audio,read,2025/01/03,2025/02/01,,1,,,,,,,,4.5,,,,,No\n";

    [Fact]
    public void GoodreadsExport_ReadsTitlesSeriesAuthorsAndIsbns()
    {
        var contents = ListFileReader.Read(GoodreadsExport);

        Assert.Equal(ListFileFormat.Csv, contents.Format);
        Assert.True(contents.HasShelfColumn);
        Assert.Empty(contents.Problems);
        Assert.Equal(["Dune", "The Talisman", "Words of Radiance"], contents.Books.Select(b => b.Title));

        var dune = contents.Books[0];
        Assert.Equal("isbn:9780441172719", dune.ExternalId);
        Assert.Equal(["Frank Herbert"], dune.Authors);
        Assert.Equal("Dune", dune.SeriesName);
        Assert.Equal(1, dune.SeriesNumber);
        Assert.Equal(["9780441172719", "0441172717"], dune.Identifiers);

        var talisman = contents.Books[1];
        Assert.Equal(["Stephen King", "Peter Straub"], talisman.Authors);
        Assert.Null(talisman.SeriesName);
        Assert.Empty(talisman.Identifiers);
        Assert.Equal("title:talisman|stephenking", talisman.ExternalId);

        Assert.Equal(("The Stormlight Archive", 2.0), (contents.Books[2].SeriesName, contents.Books[2].SeriesNumber));
    }

    [Fact]
    public void GoodreadsExport_WithAShelf_KeepsOnlyThatShelfsRows()
    {
        var contents = ListFileReader.Read(GoodreadsExport, shelf: "To-Read");

        Assert.Equal(["Dune", "Words of Radiance"], contents.Books.Select(b => b.Title));
    }

    [Fact]
    public void StoryGraphExport_SplitsCommaSeparatedAuthors_AndFiltersByReadStatus()
    {
        var contents = ListFileReader.Read(StoryGraphExport, shelf: "to-read");

        Assert.True(contents.HasShelfColumn);
        var book = Assert.Single(contents.Books);
        Assert.Equal("Good Omens", book.Title);
        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], book.Authors);
        Assert.Equal("isbn:9780060853983", book.ExternalId);
    }

    [Fact]
    public void HandMadeCsv_WithSemicolonsQuotedCommasAndABom()
    {
        const string text = "﻿title;author;series;series number\r\n"
            + "\"Mistborn: The Final Empire\";Brandon Sanderson;Mistborn;1\r\n"
            + "\"Pride, Prejudice and Zombies\";Jane Austen & Seth Grahame-Smith;;\r\n";

        var contents = ListFileReader.Read(text);

        Assert.Equal(ListFileFormat.Csv, contents.Format);
        Assert.False(contents.HasShelfColumn);
        Assert.Equal(2, contents.Books.Count);
        Assert.Equal(("Mistborn: The Final Empire", "Mistborn", 1.0), (contents.Books[0].Title, contents.Books[0].SeriesName, contents.Books[0].SeriesNumber));
        Assert.Equal("Pride, Prejudice and Zombies", contents.Books[1].Title);
        Assert.Equal(["Jane Austen", "Seth Grahame-Smith"], contents.Books[1].Authors);
    }

    [Fact]
    public void PlainText_ReadsByAndDashAuthors_SkippingCommentsAndBlankLines()
    {
        const string text = """
            # Books to find
            Stand by Me by Stephen King

            Dune - Frank Herbert
            Project Hail Mary
            Good Omens by Terry Pratchett & Neil Gaiman
            """;

        var contents = ListFileReader.Read(text);

        Assert.Equal(ListFileFormat.Text, contents.Format);
        Assert.Equal(["Stand by Me", "Dune", "Project Hail Mary", "Good Omens"], contents.Books.Select(b => b.Title));
        Assert.Equal(["Stephen King"], contents.Books[0].Authors);
        Assert.Equal(["Frank Herbert"], contents.Books[1].Authors);
        Assert.Empty(contents.Books[2].Authors);
        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], contents.Books[3].Authors);
    }

    [Fact]
    public void PlainText_GoodreadsStyleSeriesInTheTitle_IsSplitOff()
    {
        var book = Assert.Single(ListFileReader.Read("The Way of Kings (The Stormlight Archive, #1) by Brandon Sanderson").Books);

        Assert.Equal(("The Way of Kings", "The Stormlight Archive", 1.0), (book.Title, book.SeriesName, book.SeriesNumber));
    }

    [Fact]
    public void BrokenRows_AreSkippedAndReportedWithTheirLineNumbers()
    {
        const string text = "Title,Author\n"
            + "Dune,Frank Herbert\n"
            + ",Nobody\n"
            + "\"Unclosed,Someone\n"
            + "Hyperion,Dan Simmons\n";

        var contents = ListFileReader.Read(text);

        Assert.Equal(["Dune", "Hyperion"], contents.Books.Select(b => b.Title));
        Assert.Equal([new ListFileProblem(3, "it has no title"), new ListFileProblem(4, "a quote is not closed")], contents.Problems);
    }

    [Fact]
    public void QuotedFieldOverSeveralLines_KeepsTheLineNumbersOfTheRowsAfterIt()
    {
        const string text = "Title,Review\n"
            + "Dune,\"Long\nreview\"\n"
            + ",nothing\n";

        Assert.Equal([new ListFileProblem(4, "it has no title")], ListFileReader.Read(text).Problems);
    }

    [Fact]
    public void SameBookTwice_IsOneBook()
    {
        var contents = ListFileReader.Read("Dune by Frank Herbert\nDUNE by frank herbert\n");

        Assert.Single(contents.Books);
    }

    [Fact]
    public void Ids_StayTheSameWhenTheRowsAreReordered()
    {
        var first = ListFileReader.Read("Dune by Frank Herbert\nHyperion by Dan Simmons\n").Books;
        var reordered = ListFileReader.Read("Hyperion by Dan Simmons\nDune by Frank Herbert\n").Books;

        Assert.Equal(first.Select(b => b.ExternalId).Order(), reordered.Select(b => b.ExternalId).Order());
        Assert.Equal(first[0].ExternalId, reordered[1].ExternalId);
    }

    [Fact]
    public void Id_PrefersTheIsbn_AsItsIsbn13WhicheverFormTheFileHas()
    {
        Assert.Equal("isbn:9780441172719", ListFileReader.IdOf("Dune", ["Frank Herbert"], ["0441172717"]));
        Assert.Equal("isbn:9780441172719", ListFileReader.IdOf("Dune (Deluxe)", [], ["978-0-441-17271-9"]));
        Assert.Equal("title:dune|frankherbert", ListFileReader.IdOf("Dune", ["Frank Herbert"], ["not an isbn", "B00B7NPRY8"]));
    }

    [Fact]
    public void Ids_HoldNoSpaces()
    {
        Assert.DoesNotContain(' ', ListFileReader.IdOf("The Left Hand of Darkness", ["Ursula K. Le Guin"], []));
    }

    [Fact]
    public void EmptyFile_HasNoBooks()
    {
        var contents = ListFileReader.Read("");

        Assert.Empty(contents.Books);
        Assert.Empty(contents.Problems);
    }
}

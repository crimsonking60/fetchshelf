using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class FolderLayoutTests
{
    private static Dictionary<string, string> Values(
        string author = "Brandon Sanderson",
        string series = "",
        string seriesNumber = "",
        string title = "The Way of Kings",
        string edition = "",
        string narrator = "") => new()
        {
            ["author"] = author,
            ["authors"] = author,
            ["series"] = series,
            ["series_number"] = seriesNumber,
            ["title"] = title,
            ["edition"] = edition,
            ["narrator"] = narrator,
            ["language"] = "English",
            ["year"] = "2024",
        };

    private static string Render(FolderLayout layout, Dictionary<string, string> values, FileNameRules rules = FileNameRules.Posix) =>
        string.Join("/", layout.Render(values, rules));

    [Theory]
    [InlineData("", "", "", "", "Brandon Sanderson/The Way of Kings")]
    [InlineData("Stormlight Archive", "1", "", "", "Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings")]
    [InlineData("Mistborn", "", "", "", "Brandon Sanderson/Mistborn/The Way of Kings")]
    [InlineData("", "", "2nd Edition", "", "Brandon Sanderson/The Way of Kings, 2nd Edition")]
    [InlineData("", "", "", "Michael Kramer", "Brandon Sanderson/The Way of Kings {Michael Kramer}")]
    [InlineData("Stormlight Archive", "1, 2", "Revised", "Kate Reading", "Brandon Sanderson/Stormlight Archive/Stormlight Archive #1, 2 - The Way of Kings, Revised {Kate Reading}")]
    public void Default_IsTheBuiltInLayout(string series, string number, string edition, string narrator, string expected) =>
        Assert.Equal(expected, Render(FolderLayout.Default, Values(series: series, seriesNumber: number, edition: edition, narrator: narrator)));

    [Fact]
    public void Render_FolderWithAllTokensEmpty_IsLeftOut() =>
        Assert.Equal("The Way of Kings", Render(FolderLayout.Default, Values(author: "")));

    [Fact]
    public void Render_FolderWithoutTokens_IsKept() =>
        Assert.Equal("Books/Brandon Sanderson - The Way of Kings", Render(FolderLayout.Parse("Books/{author} - {title}"), Values()));

    [Fact]
    public void Render_FolderWhoseValueCleansToNothing_IsAnUnderscore() =>
        Assert.Equal("_/The Way of Kings", Render(FolderLayout.Default, Values(author: "???"), FileNameRules.Portable));

    [Fact]
    public void Render_GroupWithAnEmptyToken_IsLeftOut()
    {
        var layout = FolderLayout.Parse("{series}/[{series_number}. ]{title}[ ({year})]");

        Assert.Equal("Mistborn/The Way of Kings (2024)", Render(layout, Values(series: "Mistborn")));
        Assert.Equal("Mistborn/3. The Way of Kings (2024)", Render(layout, Values(series: "Mistborn", seriesNumber: "3")));
    }

    [Fact]
    public void Render_TheDocumentedComicsTemplate_OrdersBySeries()
    {
        var layout = FolderLayout.Parse("{series}/[{series_number} - ]{title} ({year})");

        Assert.Equal("Saga/3 - Volume Three (2024)", Render(layout, Values(series: "Saga", seriesNumber: "3", title: "Volume Three")));
        Assert.Equal("Maus (2024)", Render(layout, Values(title: "Maus")));
    }

    [Fact]
    public void Render_LiteralBracesAndBrackets_AreKept() =>
        Assert.Equal("[2024] {The Way of Kings}", Render(FolderLayout.Parse("[[{year}]] {{{title}}}"), Values()));

    [Fact]
    public void Render_ValueWithASlash_StaysInItsFolder() =>
        Assert.Equal("ACDC/The Way of Kings", Render(FolderLayout.Default, Values(author: "AC/DC")));

    [Fact]
    public void Render_TooLong_ShortensTheTitleAndKeepsTheRest()
    {
        var names = FolderLayout.Default.Render(
            Values(series: "Stormlight Archive", seriesNumber: "1", title: new string('x', 300), edition: "2nd Edition", narrator: "Michael Kramer"),
            FileNameRules.Posix);

        var last = names[^1];
        Assert.Equal(FileNameCleaner.MaxComponentBytes, System.Text.Encoding.UTF8.GetByteCount(last));
        Assert.StartsWith("Stormlight Archive #1 - xxx", last);
        Assert.EndsWith("x…, 2nd Edition {Michael Kramer}", last);
    }

    [Fact]
    public void Render_TooLongWithColons_ShortensByTheCleanedLength()
    {
        // Under portable rules each ':' becomes ' -', so the cleaned name is longer than the raw one.
        var names = FolderLayout.Default.Render(Values(title: string.Concat(Enumerable.Repeat("a:", 150)), narrator: "N"), FileNameRules.Portable);

        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(names[^1]), FileNameCleaner.MaxComponentBytes - 3, FileNameCleaner.MaxComponentBytes);
        Assert.EndsWith("… {N}", names[^1]);
    }

    [Fact]
    public void Render_TooLongWithoutRoomForTheTitle_IsCutAtTheEnd()
    {
        var names = FolderLayout.Default.Render(Values(title: "Short", narrator: new string('n', 300)), FileNameRules.Posix);

        Assert.Equal("Short {" + new string('n', FileNameCleaner.MaxComponentBytes - 7), names[^1]);
    }

    [Fact]
    public void Render_TooLongAuthor_IsCutAtTheEnd()
    {
        var names = FolderLayout.Default.Render(Values(author: new string('a', 300)), FileNameRules.Posix);

        Assert.Equal(new string('a', FileNameCleaner.MaxComponentBytes), names[0]);
    }

    [Theory]
    [InlineData("", "it is empty")]
    [InlineData("/{author}/{title}", "starts with '/'")]
    [InlineData("{author}/", "ends with '/'")]
    [InlineData("{author}//{title}", "empty folder")]
    [InlineData("{author}/{titel}", "'{titel}' is not a token")]
    [InlineData("{author}/{title", "has no closing '}'")]
    [InlineData("{author}/{title}}", "closes no '{'")]
    [InlineData("{author}/[{title}", "has no closing ']'")]
    [InlineData("{author}/{title}]", "closes no '['")]
    [InlineData("{author}/[[{title}] x]", "closes no '['")]
    [InlineData("{author}/[a[{title}]]", "can't be nested")]
    [InlineData("{author}/{title}[ - ]", "has no token")]
    [InlineData("[{author}/]{title}", "can't span folders")]
    [InlineData("{author}/{series}", "has no {title}")]
    public void Parse_InvalidTemplate_SaysWhy(string template, string expected)
    {
        var error = Assert.Throws<FormatException>(() => FolderLayout.Parse(template));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Parse_Template_KeepsItAsWritten() =>
        Assert.Equal("{authors}/{title}", FolderLayout.Parse("{authors}/{title}").Template);
}

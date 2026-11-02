using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class SeriesParserTests
{
    [Fact]
    public void Parse_MixedEntryTypes_ParsesEachIntoCorrectKind()
    {
        var series = SeriesParser.Parse("Test Series", "1, 2-3, 4p1");

        Assert.False(series.HadParseWarning);
        Assert.Equal(3, series.Entries.Count);
        Assert.Equal(SeriesEntryKind.Number, series.Entries[0].Kind);
        Assert.Equal(SeriesEntryKind.Range, series.Entries[1].Kind);
        Assert.Equal(SeriesEntryKind.Part, series.Entries[2].Kind);
    }

    [Fact]
    public void Parse_UnparseableEntry_YieldsNoEntriesWithWarning()
    {
        var series = SeriesParser.Parse("Test Series", "1, not-a-number, 3");

        Assert.Empty(series.Entries);
        Assert.True(series.HadParseWarning);
    }

    [Fact]
    public void Parse_DecimalEntries_ParseAsNumbers()
    {
        var series = SeriesParser.Parse("Test Series", "1.5, .5");

        Assert.Equal(2, series.Entries.Count);
        Assert.All(series.Entries, e => Assert.Equal(SeriesEntryKind.Number, e.Kind));
        Assert.Equal(".5", series.Entries[1].Display);
    }

    [Fact]
    public void Parse_EmptyText_YieldsNoEntriesWithoutWarning()
    {
        var series = SeriesParser.Parse("Test Series", "");

        Assert.Empty(series.Entries);
        Assert.False(series.HadParseWarning);
    }

    [Fact]
    public void SortedEntries_SameFirstNumber_OrdersNumberThenPartThenRange()
    {
        var series = new Series
        {
            Name = "Test Series",
            Entries =
            [
                SeriesEntry.ForRange(2, 3),
                SeriesEntry.ForPart(2, 1),
                SeriesEntry.ForNumber(2, "2"),
            ],
        };

        var sorted = series.SortedEntries;

        Assert.Equal("2", sorted[0].Display);
        Assert.Equal("2p1", sorted[1].Display);
        Assert.Equal("2-3", sorted[2].Display);
    }

    [Fact]
    public void Display_RangeEntry_IsFirstDashSecond()
    {
        var entry = SeriesEntry.ForRange(1, 3);

        Assert.Equal("1-3", entry.Display);
    }

    [Fact]
    public void Display_PartEntry_IsBookPPart()
    {
        var entry = SeriesEntry.ForPart(2, 1);

        Assert.Equal("2p1", entry.Display);
    }

    [Fact]
    public void ParseLine_NameAndEntries_SplitsAtTheHash()
    {
        var series = SeriesParser.ParseLine("Stormlight Archive #1,2.5");

        Assert.Equal("Stormlight Archive", series.Name);
        Assert.Equal(["1", "2.5"], series.Entries.Select(e => e.Display));
    }

    [Fact]
    public void ParseLine_NameOnly_HasNoEntries()
    {
        var series = SeriesParser.ParseLine("  Discworld  ");

        Assert.Equal("Discworld", series.Name);
        Assert.Empty(series.Entries);
    }

    [Theory]
    [InlineData("Stormlight Archive #1,2.5")]
    [InlineData("Discworld")]
    public void FormatLine_ParsedLine_GivesTheSameLineBack(string line) =>
        Assert.Equal(line, SeriesParser.FormatLine(SeriesParser.ParseLine(line)));
}

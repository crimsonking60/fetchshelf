using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class TorrentProjectionTests
{
    private static TorrentMetadata Metadata(
        IReadOnlyList<string>? authors = null, IReadOnlyList<string>? narrators = null, IReadOnlyList<Series>? series = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = "A Book",
            Authors = authors ?? [],
            Narrators = narrators ?? [],
            Series = series ?? [],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };

    [Fact]
    public void From_AuthorsAndNarrators_KeepTheirOrderAndKeys()
    {
        var projection = TorrentProjection.From(Metadata(["J. R. R. Tolkien", "Christopher Tolkien"], ["Andy Serkis"]));

        Assert.Equal(
            [
                new ProjectedPerson(PersonRole.Author, 0, "J. R. R. Tolkien", "jrrtolkien"),
                new ProjectedPerson(PersonRole.Author, 1, "Christopher Tolkien", "christophertolkien"),
                new ProjectedPerson(PersonRole.Narrator, 0, "Andy Serkis", "andyserkis"),
            ],
            projection.People);
    }

    [Fact]
    public void From_ANameListedTwiceInOneRole_IsKeptOnce_ButMayBeBothAuthorAndNarrator()
    {
        var projection = TorrentProjection.From(Metadata(["Neil Gaiman", "neil gaiman", " "], ["Neil Gaiman"]));

        Assert.Equal(
            [(PersonRole.Author, "Neil Gaiman"), (PersonRole.Narrator, "Neil Gaiman")],
            projection.People.Select(p => (p.Role, p.Name)));
    }

    [Fact]
    public void From_ASeriesWithoutANumber_HasOneRowWithoutNumbers()
    {
        var projection = TorrentProjection.From(Metadata(series: [SeriesParser.Parse("Discworld", null)]));

        var row = Assert.Single(projection.Series);
        Assert.Equal(new ProjectedSeriesEntry(0, "Discworld", "discworld", null, null, null, null), row);
    }

    [Fact]
    public void From_SeveralEntriesRangesAndParts_HaveOneRowEach_InNumberOrder()
    {
        var projection = TorrentProjection.From(Metadata(series:
        [
            SeriesParser.Parse("The Expanse", "3, 1-2"),
            SeriesParser.Parse("Stormlight", "2p1"),
        ]));

        Assert.Equal(
            [
                new ProjectedSeriesEntry(0, "The Expanse", "expanse", SeriesEntryKind.Range, 1m, 2m, "1-2"),
                new ProjectedSeriesEntry(1, "The Expanse", "expanse", SeriesEntryKind.Number, 3m, 3m, "3"),
                new ProjectedSeriesEntry(2, "Stormlight", "stormlight", SeriesEntryKind.Part, 2m, 2m, "2p1"),
            ],
            projection.Series);
    }

    [Fact]
    public void From_SeriesNamesDifferingInCaseOrPunctuation_GetTheSameKey_AndADuplicateEntryIsKeptOnce()
    {
        var projection = TorrentProjection.From(Metadata(series:
        [
            SeriesParser.Parse("Wheel of Time", "1"),
            SeriesParser.Parse("The Wheel of Time!", "1"),
            SeriesParser.Parse("   ", "4"),
        ]));

        var row = Assert.Single(projection.Series);
        Assert.Equal(("Wheel of Time", "wheel of time"), (row.Name, row.NameKey));
    }

    [Fact]
    public void From_LanguageAndMediaType_AreTheEnumNames()
    {
        var projection = TorrentProjection.From(Metadata());

        Assert.Equal(("English", "Audiobook"), (projection.Language, projection.MediaType));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("1.5", 1.5)]
    [InlineData("1-3", 3)]
    [InlineData("2p1", 2)]
    public void SeriesEntry_LastNumber_IsTheEndOfARange_ElseItsOwnNumber(string entry, double expected)
    {
        var parsed = Assert.Single(SeriesParser.Parse("S", entry).Entries);

        Assert.Equal((decimal)expected, parsed.LastNumber);
    }

    [Fact]
    public void SeriesEntry_LastNumber_IsNotStored_SoStoredMetadataReadsTheSame()
    {
        var metadata = Metadata(series: [SeriesParser.Parse("S", "1-3")]);

        var json = TorrentMetadataSerializer.Serialize(metadata);
        var read = TorrentMetadataSerializer.Deserialize(json);

        Assert.DoesNotContain("LastNumber", json);
        Assert.Equal(3m, read.Series[0].Entries[0].LastNumber);
    }
}

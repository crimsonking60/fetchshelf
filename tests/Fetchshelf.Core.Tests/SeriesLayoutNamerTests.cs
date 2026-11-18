using System.Text;
using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class SeriesLayoutNamerTests
{
    private static TorrentMetadata Build(
        IReadOnlyList<Series>? series = null,
        string title = "Saga, Volume 1",
        Edition? edition = null,
        IReadOnlyList<string>? narrators = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.ComicBook,
            LegacyCategory = LegacyCategory.EbookComicsGraphicNovels,
            Language = Language.English,
            Title = title,
            Edition = edition,
            Authors = ["Brian K. Vaughan"],
            Narrators = narrators ?? [],
            Series = series ?? [],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        };

    private static Series Saga(params SeriesEntry[] entries) => new() { Name = "Saga", Entries = entries };

    private static SeriesPlacement Compute(TorrentMetadata metadata, FileNameRules rules = FileNameRules.Posix, string extension = ".cbz") =>
        SeriesLayoutNamer.Compute("/comics", "_oneshots", metadata, rules, extension);

    [Fact]
    public void Compute_BookInASeries_GoesIntoTheSeriesFolder_NumberedAndTitled()
    {
        var place = Compute(Build([Saga(SeriesEntry.ForNumber(1, "1"))]));

        Assert.Equal(new SeriesPlacement(Path.Combine("/comics", "Saga"), "Saga #001 - Saga, Volume 1", IsOneShot: false), place);
    }

    [Fact]
    public void Compute_SeveralEntries_AreJoinedInOrder()
    {
        var place = Compute(Build([Saga(SeriesEntry.ForNumber(2, "2"), SeriesEntry.ForNumber(1, "1"))]));

        Assert.Equal("Saga #001, 002 - Saga, Volume 1", place.FileBaseName);
    }

    [Fact]
    public void Compute_SeriesWithoutEntries_LeavesTheNumberOut()
    {
        Assert.Equal("Saga - Saga, Volume 1", Compute(Build([Saga()])).FileBaseName);
    }

    [Fact]
    public void Compute_WithAnEdition_AddsIt()
    {
        var place = Compute(Build([Saga(SeriesEntry.ForNumber(1, "1"))], edition: new Edition("Deluxe Edition", 0)));

        Assert.Equal("Saga #001 - Saga, Volume 1, Deluxe Edition", place.FileBaseName);
    }

    [Fact]
    public void Compute_NarratorIsLeftOut()
    {
        var place = Compute(Build([Saga(SeriesEntry.ForNumber(1, "1"))], narrators: ["Someone"]));

        Assert.DoesNotContain("Someone", place.FileBaseName);
    }

    [Fact]
    public void Compute_NoSeries_GoesIntoTheOneShotsFolder_UnderItsTitle()
    {
        var place = Compute(Build(title: "Some Standalone Graphic Novel"));

        Assert.Equal(new SeriesPlacement(Path.Combine("/comics", "_oneshots"), "Some Standalone Graphic Novel", IsOneShot: true), place);
    }

    [Fact]
    public void Compute_TheSeriesWithEntries_IsChosenOverOneWithout()
    {
        var place = Compute(Build([new Series { Name = "Image Comics", Entries = [] }, Saga(SeriesEntry.ForNumber(3, "3"))]));

        Assert.Equal(Path.Combine("/comics", "Saga"), place.Folder);
    }

    [Fact]
    public void Compute_NamesAreCleanedByTheLibrarysRules()
    {
        var place = Compute(
            Build([new Series { Name = "Batman: Year One", Entries = [SeriesEntry.ForNumber(1, "1")] }], title: "Who Is Batman?"),
            FileNameRules.Portable);

        Assert.Equal(Path.Combine("/comics", "Batman - Year One"), place.Folder);
        Assert.Equal("Batman - Year One #001 - Who Is Batman", place.FileBaseName);
    }

    [Fact]
    public void Compute_LongTitle_IsShortened_LeavingRoomForTheExtension()
    {
        var place = Compute(Build([Saga(SeriesEntry.ForNumber(1, "1"))], title: new string('x', 400)), extension: ".cbz");

        Assert.True(Encoding.UTF8.GetByteCount(place.FileBaseName + ".cbz") <= FileNameCleaner.MaxComponentBytes);
        Assert.StartsWith("Saga #001 - xxx", place.FileBaseName);
        Assert.EndsWith("…", place.FileBaseName);
    }

    [Theory]
    [InlineData("1", "001")]
    [InlineData("9", "009")]
    [InlineData("10", "010")]
    [InlineData("99", "099")]
    [InlineData("100", "100")]
    [InlineData("1000", "1000")]
    [InlineData("1.5", "001.5")]
    [InlineData(".5", "000.5")]
    public void Padded_Number(string display, string expected) =>
        Assert.Equal(expected, SeriesLayoutNamer.Padded(SeriesEntry.ForNumber(decimal.Parse("0" + display, System.Globalization.CultureInfo.InvariantCulture), display)));

    [Fact]
    public void Padded_Range_PadsBothEnds() => Assert.Equal("001-006", SeriesLayoutNamer.Padded(SeriesEntry.ForRange(1, 6)));

    [Fact]
    public void Padded_Part_PadsTheBookNumberOnly() => Assert.Equal("002p1", SeriesLayoutNamer.Padded(SeriesEntry.ForPart(2, 1)));

    [Fact]
    public void Padded_NamesSortInSeriesOrder()
    {
        var names = new[] { 10, 2, 100, 1 }.Select(n => $"Saga #{SeriesLayoutNamer.Padded(SeriesEntry.ForNumber(n, n.ToString(System.Globalization.CultureInfo.InvariantCulture)))}").ToList();

        Assert.Equal(["Saga #001", "Saga #002", "Saga #010", "Saga #100"], names.Order(StringComparer.Ordinal));
    }
}

public class MylarSeriesJsonBuilderTests
{
    [Fact]
    public void Build_HasTheSeriesNameAsWritten_AndEveryFieldKomgaRequires()
    {
        using var json = JsonDocument.Parse(MylarSeriesJsonBuilder.Build("Batman: Year One"));
        var metadata = json.RootElement.GetProperty("metadata");

        Assert.Equal("Batman: Year One", metadata.GetProperty("name").GetString());
        Assert.Equal(1, metadata.GetProperty("volume").GetInt32());
        Assert.Equal("Continuing", metadata.GetProperty("status").GetString());
        // Komga's reader fails on a series.json without any of these.
        foreach (var field in new[] { "type", "publisher", "name", "comicid", "year", "booktype", "comic_image", "total_issues", "publication_run", "status" })
        {
            Assert.True(metadata.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null, field);
        }
    }
}

using System.Text;
using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Tests;

/// <summary>The Torrents page's export: one row per torrent, as CSV or JSON.</summary>
public sealed class LibraryExportTests
{
    private static readonly DateTimeOffset Added = new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Linked = new(2026, 11, 2, 9, 45, 5, TimeSpan.FromHours(2));

    private static LibraryExportRow Linked1() => LibraryExportRow.From(
        new TorrentEntity
        {
            Id = "hash1",
            MamId = 123456,
            Title = "The Way of Kings",
            NormalizedTitleKey = "way of kings",
            MetadataJson = "{}",
            CreatedAt = Added,
            LibraryPath = "/library/audiobooks/Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings",
            LinkerName = "Audiobooks",
            ChosenAudioExtension = "m4b",
            Language = "English",
            MediaType = "Audiobook",
        },
        OrganizerTestSupport.BuildMetadata("The Way of Kings", ["Brandon Sanderson", "Someone Else"]) with
        {
            Narrators = ["Michael Kramer", "Kate Reading"],
            Series = [new Series { Name = "Stormlight Archive", Entries = [SeriesEntry.ForNumber(1, "1")] }, new Series { Name = "Cosmere", Entries = [] }],
            TotalSizeBytes = 1_288_490_189,
        },
        "linked",
        Linked);

    private static LibraryExportRow Waiting(string title) => LibraryExportRow.From(
        new TorrentEntity
        {
            Id = "hash2",
            MamId = 7,
            Title = title,
            NormalizedTitleKey = "x",
            MetadataJson = "{}",
            CreatedAt = Added,
            LinkerName = "Audiobooks",
        },
        OrganizerTestSupport.BuildMetadata(title, []),
        "waiting",
        linked: null);

    private static async Task<string> CsvAsync(params LibraryExportRow[] rows)
    {
        await using var writer = new StringWriter();
        await LibraryExport.WriteCsvAsync(rows, writer);
        return writer.ToString();
    }

    private static async Task<JsonElement> JsonAsync(params LibraryExportRow[] rows)
    {
        using var stream = new MemoryStream();
        await LibraryExport.WriteJsonAsync(rows, stream);
        return JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())).RootElement;
    }

    [Fact]
    public async Task Csv_WritesAHeaderAndOneLinePerTorrent()
    {
        var csv = await CsvAsync(Linked1(), Waiting("Unlinked Book"));

        Assert.Equal(
            "mam_id,title,authors,narrators,series,media_type,language,format,size_bytes,library,folder,status,added,linked\r\n"
            + "123456,The Way of Kings,Brandon Sanderson; Someone Else,Michael Kramer; Kate Reading,Stormlight Archive #1; Cosmere,Audiobook,English,m4b,"
            + "1288490189,Audiobooks,/library/audiobooks/Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings,linked,"
            + "2026-11-01T08:30:00Z,2026-11-02T07:45:05Z\r\n"
            + "7,Unlinked Book,,,,Audiobook,English,,0,,,waiting,2026-11-01T08:30:00Z,\r\n",
            csv);
    }

    [Theory]
    [InlineData("Dune, Part One", "\"Dune, Part One\"")]
    [InlineData("The \"Real\" Story", "\"The \"\"Real\"\" Story\"")]
    [InlineData("Two\nLines", "\"Two\nLines\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1 Edition", "'+1 Edition")]
    [InlineData("-ish", "'-ish")]
    [InlineData("@home", "'@home")]
    public async Task Csv_QuotesAndDefusesTextFields(string title, string expected)
    {
        var csv = await CsvAsync(Waiting(title));

        Assert.StartsWith($"7,{expected},", csv.Split("\r\n")[1]);
    }

    [Fact]
    public async Task Csv_NoRows_IsTheHeaderAlone()
    {
        Assert.Equal("mam_id,title,authors,narrators,series,media_type,language,format,size_bytes,library,folder,status,added,linked\r\n", await CsvAsync());
    }

    [Fact]
    public async Task Json_IsAnArrayOfObjects_WithListsAsArrays()
    {
        var json = await JsonAsync(Linked1(), Waiting("Unlinked Book"));

        Assert.Equal(2, json.GetArrayLength());
        var first = json[0];
        Assert.Equal(123456, first.GetProperty("mam_id").GetInt64());
        Assert.Equal("The Way of Kings", first.GetProperty("title").GetString());
        Assert.Equal(["Brandon Sanderson", "Someone Else"], first.GetProperty("authors").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(["Michael Kramer", "Kate Reading"], first.GetProperty("narrators").EnumerateArray().Select(a => a.GetString()));
        var series = first.GetProperty("series");
        Assert.Equal(("Stormlight Archive", "1"), (series[0].GetProperty("name").GetString(), series[0].GetProperty("number").GetString()));
        Assert.Equal(JsonValueKind.Null, series[1].GetProperty("number").ValueKind);
        Assert.Equal(["m4b"], first.GetProperty("format").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(1_288_490_189, first.GetProperty("size_bytes").GetInt64());
        Assert.Equal(("Audiobooks", "linked"), (first.GetProperty("library").GetString(), first.GetProperty("status").GetString()));
        Assert.Equal(("2026-11-01T08:30:00Z", "2026-11-02T07:45:05Z"), (first.GetProperty("added").GetString(), first.GetProperty("linked").GetString()));

        var second = json[1];
        Assert.Equal(JsonValueKind.Null, second.GetProperty("library").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("folder").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("linked").ValueKind);
        Assert.Empty(second.GetProperty("authors").EnumerateArray());
    }

    [Fact]
    public async Task Json_ManyRows_AreAllWritten()
    {
        var rows = Enumerable.Range(1, 2000).Select(i => Waiting($"Book {i}")).ToArray();

        var json = await JsonAsync(rows);

        Assert.Equal(2000, json.GetArrayLength());
        Assert.Equal("Book 2000", json[1999].GetProperty("title").GetString());
    }

    [Fact]
    public void From_ATorrentWithoutStoredLanguageOrMediaType_UsesItsMetadata()
    {
        var row = Waiting("Old Record");

        Assert.Equal(("Audiobook", "English"), (row.MediaType, row.Language));
    }
}

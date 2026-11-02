using System.Net;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientSnatchlistTests
{
    private const string AccountStateJson = """
        {
            "ratio": "5",
            "uploaded_bytes": 1,
            "downloaded_bytes": 1,
            "wedges": 0,
            "uid": 42,
            "snatch_summary": {"unsat": {"count": 0, "limit": 50}}
        }
        """;

    private static string SnatchlistRowsJson(int count) =>
        $$"""{"success":true,"rows":[{{string.Join(',', Enumerable.Range(0, count).Select(i => $$"""{"id":{{i}},"title":"Book {{i}}","seeders":1,"leechers":0,"finished":true,"dl":"hash{{i}}"}"""))}}]}""";

    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" });

    [Fact]
    public async Task GetSnatchListAsync_OneKind_RequestsItAndReturnsOnlyThoseRows()
    {
        string? requestedUrl = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistRowsJson(2)) };
        });

        var rows = await client.GetSnatchListAsync(MamSnatchlistKind.SeedingUnsat, page: 0, DateTimeOffset.UtcNow);

        Assert.Contains("type=seedUnsat", requestedUrl);
        Assert.Contains("cdn.myanonamouse.net", requestedUrl);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task GetSnatchListAsync_PageOfMaximumSize_ReturnsAllRowsUnmodified()
    {
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistRowsJson(250)) };
        });

        var rows = await client.GetSnatchListAsync(MamSnatchlistKind.Unsat, page: 0, DateTimeOffset.UtcNow);

        Assert.Equal(250, rows.Count);
    }

    /// <summary>A row with the metadata fields, numbers partly sent as strings as MAM does.</summary>
    private const string DetailedRowJson = """
        {"success":true,"rows":[{
            "id":"77","title":"The Way of Kings","seeders":"12","leechers":0,"finished":1,"dl":"hash77",
            "category":"39","catname":"Audiobooks - General Fiction",
            "categories":[{"id":"13","name":"Fantasy"}],
            "author":[{"id":"9","name":"Second Author"},{"id":"3","name":"Brandon Sanderson"}],
            "narrator":[{"id":"5","name":"Michael Kramer"}],
            "series":[{"id":"20","name":"The Stormlight Archive","number":"1"}],
            "fileTypes":[{"id":"1","name":"m4b"}],
            "size":"1.2 GiB","tags":"Unabridged","browseFlags":"0",
            "free":0,"vip":"1","vip_expire":null,"personalFree":0,
            "uploaderName":"someone","uploaderID":"123","times_completed":"456"
        }]}
        """;

    private static async Task<MamSnatchlistRow> ReadSingleRowAsync(string json)
    {
        var client = CreateClient(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("jsonLoad.php") ? AccountStateJson : json),
        });

        return Assert.Single(await client.GetSnatchListAsync(MamSnatchlistKind.SeedingSatisfied, page: 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task GetSnatchListAsync_RowWithMetadata_ReadsTheDetails()
    {
        var row = await ReadSingleRowAsync(DetailedRowJson);

        var details = Assert.IsType<MamSnatchlistDetails>(row.Details);
        Assert.Equal(39, details.LegacyCategoryId);
        Assert.Equal([13], details.GenreCategoryIds);
        Assert.Equal(["Brandon Sanderson", "Second Author"], details.Authors.OrderBy(a => a.Key).Select(a => a.Value));
        Assert.Equal("Michael Kramer", Assert.Single(details.Narrators).Value);
        Assert.Equal(new MamSeriesInfo("The Stormlight Archive", "1"), Assert.Single(details.Series));
        Assert.Equal(["m4b"], details.FileTypes);
        Assert.Equal("1.2 GiB", details.SizeRaw);
        Assert.True(details.IsVip);
        Assert.Equal(0, details.VipExpireUnixSeconds);
        Assert.Equal("someone", details.UploaderName);
        Assert.Equal(456, details.TimesCompleted);
        Assert.Equal(12, row.Seeders);
    }

    [Fact]
    public async Task GetSnatchListAsync_RowWithoutMetadata_HasNoDetails()
    {
        var row = await ReadSingleRowAsync(SnatchlistRowsJson(1));

        Assert.Null(row.Details);
    }

    [Fact]
    public async Task RowMapper_SameTorrentAsASearchRecord_GivesTheSameMetadata()
    {
        var row = await ReadSingleRowAsync(DetailedRowJson);
        var stored = MamTorrentMapper.Map(new MamTorrentRecord
        {
            MamId = 77,
            Title = "The Way of Kings",
            Authors = new Dictionary<long, string> { [3] = "Brandon Sanderson", [9] = "Second Author" },
            Narrators = new Dictionary<long, string> { [5] = "Michael Kramer" },
            Series = [new MamSeriesInfo("The Stormlight Archive", "1")],
            Description = "Kept from the search.",
            Isbn = "ASIN:B003P2WO5E",
            MediaTypeId = (int)MediaType.Audiobook,
            OldMainCategoryId = 13,
            FictionCategoryId = 1,
            LegacyCategoryId = 39,
            GenreCategoryIds = [13],
            LanguageId = (int)Language.English,
            FlagsRaw = 0,
            FileTypesRaw = "m4b",
            FileCount = 40,
            SizeRaw = "1.2 GiB",
            TagsRaw = "Unabridged",
            UploaderId = 123,
            UploaderName = "someone",
            UploadedAt = new DateTimeOffset(2020, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Seeders = 3,
            Leechers = 0,
            TimesCompleted = 400,
            IsFree = false,
            IsPersonalFreeleech = false,
            IsFreeleechVip = false,
            IsVip = true,
            VipExpireUnixSeconds = 0,
        });

        var fresh = MamSnatchlistRowMapper.Map(row, row.Details!, stored);

        Assert.Equal(TorrentMetadataSerializer.Serialize(stored), TorrentMetadataSerializer.Serialize(fresh));
    }
}

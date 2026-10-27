using System.Net;
using System.Text;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.Notion;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class NotionListImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-notion-list-importer-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string AccountStateJson = """
        {
            "ratio": 5.0,
            "uploaded_bytes": 1000,
            "downloaded_bytes": 100,
            "wedges": 10,
            "uid": 1,
            "snatch_summary": { "unsat": { "count": 0, "limit": 20 } }
        }
        """;

    private const string EmptyResultJson = """{"perpage":100,"start":0,"total":0,"found":0,"data":[]}""";

    private static string SearchResultWithOneAudiobook(long mamId, bool free = true) => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": {{mamId}}, "title": "Mistborn", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dl1"
                }
            ]
        }
        """;

    private static string RowsWithMamField(params long[] mamIds)
    {
        var rows = mamIds.Select((mamId, index) => $$"""
            {
                "id": "row-{{index}}",
                "properties": {
                    "MaM": { "id": "prop-{{index}}", "type": "url", "url": "https://www.myanonamouse.net/t/{{mamId}}" }
                }
            }
            """);
        return $"{{\"results\":[{string.Join(',', rows)}],\"next_cursor\":null,\"has_more\":false}}";
    }

    private sealed record Harness(NotionListImporter Importer, FetchshelfDbContext Context, List<string> SearchRequestPaths, List<TimeSpan> Delays);

    private Harness CreateHarness(string notionResponseJson, Func<long, string> searchResponseForId)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var searchRequestPaths = new List<string>();

        var mamHandler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                searchRequestPaths.Add(path);
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = System.Text.Json.JsonDocument.Parse(body);
                var mamId = document.RootElement.GetProperty("tor").GetProperty("id").GetInt64();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchResponseForId(mamId)) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var mamClient = new MamClient(new HttpClient(mamHandler), new MamClientOptions { MamId = "cookie" });

        var notionHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(notionResponseJson, Encoding.UTF8, "application/json"),
        });
        var notionClient = new NotionClient(new HttpClient(notionHandler));

        var delays = new List<TimeSpan>();
        var importer = new NotionListImporter(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            notionClient,
            delay: (wait, _) => { delays.Add(wait); return Task.CompletedTask; });

        return new Harness(importer, context, searchRequestPaths, delays);
    }

    private static NotionListEntry Entry(params GrabEntry[] grab) => new()
    {
        DataSource = "data-source-1",
        Token = "secret-token",
        Name = "Want to read",
        MamFields = ["MaM"],
        Grab = grab.Length == 0 ? [new GrabEntry { Cost = ConfiguredCost.Free }] : [.. grab],
    };

    [Fact]
    public async Task RunAsync_UnknownId_FetchesAndSelects()
    {
        var harness = CreateHarness(RowsWithMamField(1), mamId => SearchResultWithOneAudiobook(mamId));

        var selectedCount = await harness.Importer.RunAsync(
            Entry(new GrabEntry { Cost = ConfiguredCost.Ratio }), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(1, selectedCount);
        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal(1, selected.MamId);
    }

    [Fact]
    public async Task RunAsync_AlreadyKnownId_IsNotRefetchedFromMam()
    {
        var harness = CreateHarness(RowsWithMamField(42), mamId => SearchResultWithOneAudiobook(mamId));
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "existing-hash",
            MamId = 42,
            NormalizedTitleKey = "mistborn",
            Title = "Mistborn",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(new TorrentMetadata
            {
                MamId = 42,
                VipStatus = VipStatus.NotVip,
                MediaType = MediaType.Audiobook,
                LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
                Language = Language.English,
                Title = "Mistborn",
                MetadataSource = MetadataSource.Mam,
                UploadTime = DateTimeOffset.UtcNow,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();

        await harness.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Empty(harness.SearchRequestPaths);
    }

    [Fact]
    public async Task RunAsync_FirstGrabBlockDoesNotMatch_SecondBlockWinsWithItsOwnCost()
    {
        var nonMatchingBlock = new GrabEntry { Cost = ConfiguredCost.Free, MinSeeders = 1000 };
        var matchingBlock = new GrabEntry { Cost = ConfiguredCost.Ratio };
        var harness = CreateHarness(RowsWithMamField(7), mamId => SearchResultWithOneAudiobook(mamId));

        await harness.Importer.RunAsync(Entry(nonMatchingBlock, matchingBlock), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal(7, selected.MamId);
    }

    [Fact]
    public async Task RunAsync_NoGrabBlockMatches_TakesNoSelectionAction()
    {
        var nonMatchingBlock = new GrabEntry { Cost = ConfiguredCost.Free, MinSeeders = 1000 };
        var harness = CreateHarness(RowsWithMamField(9), mamId => SearchResultWithOneAudiobook(mamId));

        var selectedCount = await harness.Importer.RunAsync(Entry(nonMatchingBlock), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(0, selectedCount);
        Assert.Empty(harness.Context.SelectedTorrents);
    }

    [Fact]
    public async Task RunAsync_PolitenessDelay_InvokedBetweenRows()
    {
        var harness = CreateHarness(RowsWithMamField(1, 2), mamId => SearchResultWithOneAudiobook(mamId));

        await harness.Importer.RunAsync(Entry(new GrabEntry { Cost = ConfiguredCost.Ratio }), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(2, harness.Delays.Count);
        Assert.All(harness.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(400), d));
    }
}

using System.Net;
using Fetchshelf.Clients.Goodreads;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class GoodreadsListImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-goodreads-list-importer-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string ListUrl = "https://www.goodreads.com/review/list_rss/1?shelf=to-read";

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

    private static string FeedWithOneItem(string title = "Mistborn") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>My bookshelf: to-read</title>
            <item>
              <guid>https://www.goodreads.com/review/show/1-mistborn</guid>
              <title>{title}</title>
              <author_name>Brandon Sanderson</author_name>
              <isbn>9780765311788</isbn>
              <book_large_image_url>https://images.example.com/mistborn.jpg</book_large_image_url>
              <description></description>
            </item>
          </channel>
        </rss>
        """;

    private static string SearchResultWithOneAudiobook(long mamId = 1, long seeders = 5, bool free = true) => $$"""
        {
            "perpage": 100,
            "start": 0,
            "total": 1,
            "found": 1,
            "data": [
                {
                    "id": {{mamId}}, "title": "Mistborn", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": {{seeders}}, "leechers": 1, "times_completed": 1,
                    "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dl1"
                }
            ]
        }
        """;

    private sealed record Harness(GoodreadsListImporter Importer, FetchshelfDbContext Context, List<string> SearchRequestPaths, List<TimeSpan> Delays);

    private Harness CreateHarness(string feedXml, Func<int, string> searchResponseForCall)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var searchRequestPaths = new List<string>();
        var searchCallCount = 0;

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
                var body = searchResponseForCall(searchCallCount);
                searchCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var mamClient = new MamClient(new HttpClient(mamHandler), new MamClientOptions { MamId = "cookie" });

        var goodreadsHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feedXml) });
        var goodreadsClient = new GoodreadsClient(new HttpClient(goodreadsHandler), new GoodreadsClientOptions());

        var delays = new List<TimeSpan>();
        var importer = new GoodreadsListImporter(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            goodreadsClient, SelectionTestSupport.PreferenceLists,
            delay: (wait, _) => { delays.Add(wait); return Task.CompletedTask; });

        return new Harness(importer, context, searchRequestPaths, delays);
    }

    private static GrabEntry AudioOnlyGrab() => new()
    {
        Cost = ConfiguredCost.Free,
        Categories = new CategoriesConfig { Audio = CategoryListOrBool.All, Ebook = CategoryListOrBool.None },
    };

    private static GoodreadsListEntry Entry(FilterMainType? preferFormat = null, params GrabEntry[] grab) => new()
    {
        Url = ListUrl,
        Name = "To read",
        PreferFormat = preferFormat,
        Grab = grab.Length == 0 ? [new GrabEntry { Cost = ConfiguredCost.Free }] : [.. grab],
    };

    [Fact]
    public async Task RunAsync_NewItem_CreatesListAndListItem()
    {
        var harness = CreateHarness(FeedWithOneItem(), _ => EmptyResultJson);

        await harness.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        var list = Assert.Single(harness.Context.Lists);
        Assert.Equal("1:to-read", list.Id);
        var item = Assert.Single(harness.Context.ListItems);
        Assert.Equal("Mistborn", item.BookTitle);
    }

    [Fact]
    public async Task RunAsync_ReimportedFeedWithChangedTitle_UpdatesInPlaceRatherThanDuplicating()
    {
        var harness = CreateHarness(FeedWithOneItem("Mistborn"), _ => EmptyResultJson);
        await harness.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        var harness2 = CreateHarness(FeedWithOneItem("Mistborn: The Final Empire"), _ => EmptyResultJson);
        await harness2.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        var item = Assert.Single(harness2.Context.ListItems);
        Assert.Equal("Mistborn: The Final Empire", item.BookTitle);
    }

    [Fact]
    public async Task RunAsync_BothSidesAlreadyResolved_PerformsNoSearch()
    {
        var harness = CreateHarness(FeedWithOneItem(), _ => EmptyResultJson);
        var listId = GoodreadsListId.Derive(ListUrl);
        harness.Context.ListItems.Add(new ListItemEntity
        {
            ListId = listId,
            Guid = "https://www.goodreads.com/review/show/1-mistborn",
            BookTitle = "Mistborn",
            AuthorsJson = "[]",
            CoverUrl = string.Empty,
            AllowAudio = true,
            AllowEbook = false,
            AudioStatus = "Selected",
            AudioMamId = 1,
            AudioAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();

        await harness.Importer.RunAsync(Entry(grab: [AudioOnlyGrab()]), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Empty(harness.SearchRequestPaths);
    }

    [Fact]
    public async Task RunAsync_MarkedDone_PerformsNoSearchEvenIfUnresolved()
    {
        var harness = CreateHarness(FeedWithOneItem(), _ => EmptyResultJson);
        var listId = GoodreadsListId.Derive(ListUrl);
        harness.Context.ListItems.Add(new ListItemEntity
        {
            ListId = listId,
            Guid = "https://www.goodreads.com/review/show/1-mistborn",
            BookTitle = "Mistborn",
            AuthorsJson = "[]",
            CoverUrl = string.Empty,
            AllowAudio = true,
            AllowEbook = false,
            MarkedDoneAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();

        await harness.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Empty(harness.SearchRequestPaths);
    }

    [Fact]
    public async Task RunAsync_MatchingTorrentAlreadyInLibrary_MarksExistingWithoutMamSearch()
    {
        var harness = CreateHarness(FeedWithOneItem(), _ => EmptyResultJson);
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "abcd1234",
            MamId = 42,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize("Mistborn"),
            Title = "Mistborn",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(new TorrentMetadata
            {
                MamId = 42,
                VipStatus = VipStatus.NotVip,
                MediaType = MediaType.Audiobook,
                LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
                Language = Language.English,
                Title = "Mistborn",
                Authors = ["Brandon Sanderson"],
                MetadataSource = MetadataSource.Mam,
                UploadTime = DateTimeOffset.UtcNow,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();

        await harness.Importer.RunAsync(Entry(grab: [AudioOnlyGrab()]), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        var item = Assert.Single(harness.Context.ListItems);
        Assert.Equal("Existing", item.AudioStatus);
        Assert.Equal(42, item.AudioMamId);
        Assert.Empty(harness.SearchRequestPaths);
    }

    [Fact]
    public async Task RunAsync_FirstGrabBlockFindsNothing_SecondGrabBlockWinsWithItsOwnCost()
    {
        var excludingBlock = new GrabEntry { Cost = ConfiguredCost.Free, MinSeeders = 1000 };
        var includingBlock = new GrabEntry { Cost = ConfiguredCost.Ratio };
        var harness = CreateHarness(FeedWithOneItem(), call => call < 2 ? SearchResultWithOneAudiobook() : EmptyResultJson);

        await harness.Importer.RunAsync(Entry(grab: [excludingBlock, includingBlock]), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(2, harness.SearchRequestPaths.Count);
        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal(1, selected.MamId);
        var item = Assert.Single(harness.Context.ListItems);
        Assert.Equal("Selected", item.AudioStatus);
    }

    [Fact]
    public async Task RunAsync_FreeOnlyGrabRejectsNonFreeCandidate_RecordsWantedNotSelected()
    {
        var harness = CreateHarness(FeedWithOneItem(), _ => SearchResultWithOneAudiobook(free: false));

        await harness.Importer.RunAsync(
            Entry(grab: [new GrabEntry { Cost = ConfiguredCost.Free }]), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Empty(harness.Context.SelectedTorrents);
        var item = Assert.Single(harness.Context.ListItems);
        Assert.Equal("Wanted", item.AudioStatus);
        Assert.Equal(1, item.AudioMamId);
    }

    [Fact]
    public async Task RunAsync_PolitenessDelay_InvokedBetweenItems()
    {
        var feedWithTwoItems = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>My bookshelf: to-read</title>
                <item>
                  <guid>https://www.goodreads.com/review/show/1-mistborn</guid>
                  <title>Mistborn</title>
                  <author_name>Brandon Sanderson</author_name>
                  <description></description>
                </item>
                <item>
                  <guid>https://www.goodreads.com/review/show/2-warbreaker</guid>
                  <title>Warbreaker</title>
                  <author_name>Brandon Sanderson</author_name>
                  <description></description>
                </item>
              </channel>
            </rss>
            """;
        var harness = CreateHarness(feedWithTwoItems, _ => EmptyResultJson);

        await harness.Importer.RunAsync(Entry(), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(2, harness.Delays.Count);
        Assert.All(harness.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(400), d));
    }
}

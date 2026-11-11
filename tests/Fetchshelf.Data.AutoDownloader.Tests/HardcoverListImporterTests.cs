using System.Net;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class HardcoverListImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-hardcover-list-importer-tests-").FullName;

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

    private static string Record(long mamId, string title, string author, bool ebook = false, bool free = true) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
            "mediatype": {{(ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1, "category": {{(ebook ? 60 : 39)}}, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "{{(ebook ? "epub" : "m4b")}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
            "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private static string Results(params string[] records) =>
        $$"""{"perpage":100,"start":0,"total":{{records.Length}},"found":{{records.Length}},"data":[{{string.Join(",", records)}}]}""";

    private static HardcoverBook Book(long id, string title, string author = "Frank Herbert") => new()
    {
        Id = id,
        Title = title,
        Slug = title.ToLowerInvariant().Replace(' ', '-'),
        Authors = [author],
        Series = [],
        CoverUrl = $"https://assets.example/{id}.jpg",
    };

    private sealed class FakeBookSource : IHardcoverBookSource
    {
        public List<HardcoverBook> Books { get; set; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<HardcoverBook>> GetBooksAsync(HardcoverListEntry entry, CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult<IReadOnlyList<HardcoverBook>>(Books) : Task.FromException<IReadOnlyList<HardcoverBook>>(Failure);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness(FetchshelfDbContext context, FakeBookSource books, Clock clock, List<string> searches, Func<FetchshelfDbContext, HardcoverListImporter> createImporter)
    {
        public FetchshelfDbContext Context => context;

        public FakeBookSource Books => books;

        public Clock Clock => clock;

        /// <summary>The search text of every MAM search, in order.</summary>
        public List<string> Searches => searches;

        public Task<HardcoverListRunSummary> RunAsync(HardcoverListEntry entry) =>
            createImporter(context).RunAsync(entry, unsatBuffer: 0, ignoreTorrents: []);
    }

    /// <summary>A harness whose MAM search answers with <paramref name="searchResponse"/> for each search text.</summary>
    private Harness CreateHarness(Func<string, string> searchResponse)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var searches = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                searches.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchResponse(body)) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var mamClient = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
        var books = new FakeBookSource();
        var clock = new Clock(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        return new Harness(context, books, clock, searches, ctx => new HardcoverListImporter(
            ctx, mamClient, new MamAccessGate(),
            new SelectionRunner(ctx, new EventStore(ctx), [], SelectionTestSupport.PreferenceLists),
            books, SelectionTestSupport.PreferenceLists, clock,
            delay: (_, _) => Task.CompletedTask));
    }

    private static HardcoverListEntry Entry(FilterMainType? preferFormat = null, bool dryRun = false, params GrabEntry[] grab) => new()
    {
        Name = "Want to Read",
        Status = HardcoverListStatus.WantToRead,
        PreferFormat = preferFormat,
        DryRun = dryRun,
        Grab = grab.Length == 0 ? [new GrabEntry { Cost = ConfiguredCost.Free }] : [.. grab],
    };

    private static GrabEntry AudioOnly(ConfiguredCost cost = ConfiguredCost.Free) => new()
    {
        Cost = cost,
        Categories = new CategoriesConfig { Audio = CategoryListOrBool.All, Ebook = CategoryListOrBool.None },
    };

    private ListItemEntity Item(Harness harness, long bookId) =>
        harness.Context.ListItems.AsNoTracking().Single(i => i.ExternalId == bookId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task RunAsync_NewBooks_AreStoredInHardcoversOrder_AndMarkedNotFound()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Books = [Book(2, "Dune Messiah"), Book(1, "Dune")];

        await harness.RunAsync(Entry());

        var list = Assert.Single(harness.Context.Lists);
        Assert.Equal("hardcover:status:want_to_read", list.Id);
        Assert.Equal("Want to Read", list.Name);
        var items = harness.Context.ListItems.AsNoTracking().OrderBy(i => i.Position).ToList();
        Assert.Equal(["Dune Messiah", "Dune"], items.Select(i => i.BookTitle));
        Assert.Equal("[\"Frank Herbert\"]", items[0].AuthorsJson);
        Assert.Equal("https://hardcover.app/books/dune-messiah", items[0].BookUrl);
        Assert.All(items, i => Assert.Equal(ListItemSideStatus.NotFound, i.AudioStatus));
        Assert.All(items, i => Assert.Equal(ListItemSideStatus.NotFound, i.EbookStatus));
        Assert.All(items, i => Assert.Equal(harness.Clock.Now, i.LastSearchedAt));
    }

    [Fact]
    public async Task RunAsync_FreeAudiobookOnMam_IsSelected()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(1, summary.Selected);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents).MamId);
        var item = Item(harness, 1);
        Assert.Equal(ListItemSideStatus.Selected, item.AudioStatus);
        Assert.Equal(11, item.AudioMamId);
        Assert.False(item.AllowEbook);
        Assert.Null(item.EbookStatus);
    }

    [Fact]
    public async Task RunAsync_ListWithACategory_SelectsWithThatCategory()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        var entry = Entry(grab: AudioOnly());
        entry.Category = "MAM_fiction";

        await harness.RunAsync(entry);

        Assert.Equal("MAM_fiction", Assert.Single(harness.Context.SelectedTorrents).Category);
    }

    [Fact]
    public async Task RunAsync_DifferentBookOnMam_IsNotTaken()
    {
        var harness = CreateHarness(_ => Results(Record(11, "The Dune Encyclopedia Companion Guide", "Someone Else")));
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(0, summary.Selected);
        Assert.Empty(harness.Context.SelectedTorrents);
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_TorrentCostingMoreThanAllowed_IsWanted()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert", free: false)));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly(ConfiguredCost.Free)));

        Assert.Empty(harness.Context.SelectedTorrents);
        Assert.Equal(ListItemSideStatus.Wanted, Item(harness, 1).AudioStatus);
        Assert.Equal(11, Item(harness, 1).AudioMamId);
    }

    [Theory]
    [InlineData(ConfiguredCost.Ratio, "Ratio")]
    [InlineData(ConfiguredCost.Wedge, "UseWedge")]
    public async Task RunAsync_FreeBlockFirst_LeavesANonFreeTorrentToTheNextBlock(ConfiguredCost next, string costDecision)
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert", free: false)));
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: [AudioOnly(ConfiguredCost.Free), AudioOnly(next)]));

        Assert.Equal(1, summary.Selected);
        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal(11, selected.MamId);
        Assert.Equal(costDecision, selected.CostDecision);
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_FreeBlockFirst_TakesAFreeUploadOverOneThatIsNot()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert", free: false), Record(12, "Dune", "Frank Herbert", free: true)));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: [AudioOnly(ConfiguredCost.Free), AudioOnly(ConfiguredCost.Ratio)]));

        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal(12, selected.MamId);
        Assert.Equal("GlobalFreeleech", selected.CostDecision);
    }

    [Fact]
    public async Task RunAsync_BookAlreadyInTheLibrary_IsExisting_WithoutSearchingMam()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        var metadata = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 5, title: "Dune", authorName: "Frank Herbert"));
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 5,
            NormalizedTitleKey = TitleNormalizer.Normalize("Dune"),
            Title = "Dune",
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        var item = Item(harness, 1);
        Assert.Equal(ListItemSideStatus.Existing, item.AudioStatus);
        Assert.Equal(5, item.AudioMamId);
        Assert.Empty(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_BookNotFound_IsSearchedAgainOnlyAfterADay()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));
        harness.Clock.Now += TimeSpan.FromHours(23);
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Single(harness.Searches);

        harness.Clock.Now += TimeSpan.FromHours(2);
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(2, harness.Searches.Count);
    }

    [Fact]
    public async Task RunAsync_SelectedBook_IsNeverSearchedAgain()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));
        harness.Clock.Now += TimeSpan.FromDays(3);
        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_BookLeavingTheShelf_IsMarkedRemoved_AndBackWhenItReturns()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune"), Book(2, "Children of Dune")];
        await harness.RunAsync(Entry());

        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry());
        Assert.Equal(harness.Clock.Now, Item(harness, 2).RemovedAt);

        harness.Books.Books = [Book(1, "Dune"), Book(2, "Children of Dune")];
        await harness.RunAsync(Entry());
        Assert.Null(Item(harness, 2).RemovedAt);
    }

    [Fact]
    public async Task RunAsync_BothFormatsFound_TakesOnlyThePreferredOne()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert"), Record(12, "Dune", "Frank Herbert", ebook: true)));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(preferFormat: FilterMainType.Audio));

        var item = Item(harness, 1);
        Assert.Equal(ListItemSideStatus.Selected, item.AudioStatus);
        Assert.Equal(ListItemSideStatus.NotWanted, item.EbookStatus);
        Assert.Equal(12, item.EbookMamId);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents).MamId);
    }

    [Fact]
    public async Task RunAsync_DryRun_WritesNothing()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(dryRun: true, grab: AudioOnly()));

        Assert.Equal((1, 0, true), (summary.Selected, summary.SelectedForDownload, summary.DryRun));
        Assert.Single(harness.Searches);
        Assert.Empty(harness.Context.Lists.AsNoTracking());
        Assert.Empty(harness.Context.ListItems.AsNoTracking());
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
    }

    [Fact]
    public async Task RunAsync_MarkedDoneBook_IsSkipped()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        harness.Context.Lists.Add(new ListEntity { Id = "hardcover:status:want_to_read", Name = "Want to Read", ListType = "Hardcover", CreatedAt = DateTimeOffset.UtcNow });
        harness.Context.ListItems.Add(new ListItemEntity
        {
            ListId = "hardcover:status:want_to_read",
            ExternalId = "1",
            BookTitle = "Dune",
            AuthorsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            MarkedDoneAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Empty(harness.Searches);
        Assert.Empty(harness.Context.SelectedTorrents);
    }

    [Fact]
    public async Task RunAsync_OneBookFailing_IsCounted_AndTheRestStillRun()
    {
        var harness = CreateHarness(body => body.Contains("Messiah", StringComparison.Ordinal)
            ? "not json"
            : Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(2, "Dune Messiah"), Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Selected);
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 1).AudioStatus);
        Assert.Null(Item(harness, 2).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_HardcoverFailing_FailsTheRun_WithoutAskingMam()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Failure = new HardcoverAuthenticationException("Hardcover rejected the token.");

        await Assert.ThrowsAsync<HardcoverAuthenticationException>(() => harness.RunAsync(Entry()));

        Assert.Empty(harness.Searches);
    }
}

using System.Net;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Lists;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class ListImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-list-importer-tests-").FullName;

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

    private static string Record(long mamId, string title, string author, bool ebook = false, bool free = true, string? isbn = null, string? fileType = null) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{}", "isbn": {{(isbn is null ? "null" : $"\"{isbn}\"")}}, "description": null,
            "mediatype": {{(ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1, "category": {{(ebook ? 60 : 39)}}, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "{{fileType ?? (ebook ? "epub" : "m4b")}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
            "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private static string Results(params string[] records) =>
        $$"""{"perpage":100,"start":0,"total":{{records.Length}},"found":{{records.Length}},"data":[{{string.Join(",", records)}}]}""";

    private static ListBook Book(long id, string title, string author = "Frank Herbert", params string[] identifiers) => HardcoverListSource.ToListBook(new()
    {
        Id = id,
        Title = title,
        Slug = title.ToLowerInvariant().Replace(' ', '-'),
        Authors = [author],
        Series = [],
        CoverUrl = $"https://assets.example/{id}.jpg",
        Identifiers = identifiers,
    });

    /// <summary>A full page of results for other books: 100 of them.</summary>
    private static string FullPageOfOtherBooks() =>
        Results(Enumerable.Range(1, 100).Select(i => Record(1000 + i, $"Other Book {i}", "Someone Else")).ToArray());

    private sealed class FakeBookSource : IListBookSource
    {
        public List<ListBook> Books { get; set; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<ListBook>> GetBooksAsync(CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult<IReadOnlyList<ListBook>>(Books) : Task.FromException<IReadOnlyList<ListBook>>(Failure);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness(
        FetchshelfDbContext context, FakeBookSource books, Clock clock, List<string> searches, Func<FetchshelfDbContext, ListImporter> createImporter,
        MamClient mamClient)
    {
        /// <summary>The MAM bookmarks, read through the harness's MAM: a search with <c>"searchIn":"bookmarks"</c>.</summary>
        public MamBookmarksSource Bookmarks { get; } = new(mamClient, new MamAccessGate(), "MAM bookmarks", delay: (_, _) => Task.CompletedTask);

        public FetchshelfDbContext Context => context;

        public FakeBookSource Books => books;

        public Clock Clock => clock;

        /// <summary>The search text of every MAM search, in order.</summary>
        public List<string> Searches => searches;

        public Task<ListRunSummary> RunAsync(HardcoverListEntry entry) =>
            createImporter(context).RunAsync(ListDefinition.ForHardcover(entry, 0), books, unsatBuffer: 0, ignoreTorrents: []);

        public Task<ListRunSummary> RunAsync(ListDefinition list, IListBookSource source) =>
            createImporter(context).RunAsync(list, source, unsatBuffer: 0, ignoreTorrents: []);
    }

    /// <summary>A harness whose MAM search answers with <paramref name="searchResponse"/> for each search text.</summary>
    private Harness CreateHarness(Func<string, string> searchResponse, SelectionLibraries? libraries = null) =>
        CreateHarness(body => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchResponse(body)) }, libraries);

    /// <summary>A harness whose MAM search answers with <paramref name="searchResponse"/>'s whole response, status and all.</summary>
    private Harness CreateHarness(Func<string, HttpResponseMessage> searchResponse, SelectionLibraries? libraries = null)
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
                return searchResponse(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var mamClient = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        var books = new FakeBookSource();
        var clock = new Clock(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        return new Harness(context, books, clock, searches, ctx => new ListImporter(
            ctx, mamClient, new MamAccessGate(),
            new SelectionRunner(ctx, new EventStore(ctx), [], SelectionTestSupport.PreferenceLists, libraries),
            SelectionTestSupport.PreferenceLists, clock,
            delay: (_, _) => Task.CompletedTask), mamClient);
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
    public async Task RunAsync_BookFailsThenWorks_RecordsItsErrorAndClearsItOnTheNextRun()
    {
        var broken = true;
        var harness = CreateHarness(_ => broken ? "not json" : EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(1, summary.Failed);
        var error = Assert.Single(harness.Context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.HardcoverList, "hardcover:status:want_to_read 1", "Dune"), (error.Stage, error.Key, error.Title));

        broken = false;
        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Empty(harness.Context.ErroredTorrents.AsNoTracking());
    }

    [Fact]
    public async Task RunAsync_MamRefusesTheSession_FailsTheRunWithoutAnErrorPerBook()
    {
        var harness = CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        harness.Books.Books = [Book(1, "Dune"), Book(2, "Dune Messiah")];

        await Assert.ThrowsAsync<MamForbiddenException>(() => harness.RunAsync(Entry(grab: AudioOnly())));

        Assert.Empty(harness.Context.ErroredTorrents.AsNoTracking());
    }

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
    public async Task RunAsync_UploadWithTheBooksIsbn_IsSelected_HoweverFarItsTitleIs()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Der Weg der Koenige", "Brandon Sanderson", isbn: "0-7653-2635-3")));
        harness.Books.Books = [Book(1, "The Way of Kings", "Brandon Sanderson", "9780765326355")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(1, summary.Selected);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents).MamId);
        Assert.Equal("[\"0765326353\",\"9780765326355\"]", Item(harness, 1).IdentifiersJson);
    }

    [Fact]
    public async Task RunAsync_AnUploadInAFormatItsLibraryDoesntLink_GivesWayToOneItDoes()
    {
        // The PDF has the book's ISBN, which would make it the book whatever else MAM has; its library takes only epub.
        var libraries = new SelectionLibraries(
            [new SelectionLibrary(new Fetchshelf.Core.Organizer.LibraryMatchRule { Category = "ebooks" }, new LibraryFormatLists(null, ["epub"]))],
            (_, path) => path);
        var harness = CreateHarness(
            _ => Results(
                Record(11, "The Way of Kings", "Brandon Sanderson", ebook: true, isbn: "9780765326355", fileType: "pdf"),
                Record(12, "The Way of Kings", "Brandon Sanderson", ebook: true)),
            libraries);
        harness.Books.Books = [Book(1, "The Way of Kings", "Brandon Sanderson", "9780765326355")];
        var entry = Entry(grab: new GrabEntry { Cost = ConfiguredCost.Free });
        entry.Category = "ebooks";

        var summary = await harness.RunAsync(entry);

        Assert.Equal(1, summary.Selected);
        Assert.Equal(12, Assert.Single(harness.Context.SelectedTorrents).MamId);
    }

    [Fact]
    public async Task RunAsync_OnlyUploadsItsLibraryDoesntLink_IsNotFound()
    {
        var libraries = new SelectionLibraries(
            [new SelectionLibrary(new Fetchshelf.Core.Organizer.LibraryMatchRule { Category = "ebooks" }, new LibraryFormatLists(null, ["epub"]))],
            (_, path) => path);
        var harness = CreateHarness(_ => Results(Record(11, "The Way of Kings", "Brandon Sanderson", ebook: true, fileType: "pdf")), libraries);
        harness.Books.Books = [Book(1, "The Way of Kings", "Brandon Sanderson")];
        var entry = Entry(grab: new GrabEntry { Cost = ConfiguredCost.Free });
        entry.Category = "ebooks";

        var summary = await harness.RunAsync(entry);

        Assert.Equal(0, summary.Selected);
        Assert.Empty(harness.Context.SelectedTorrents);
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).EbookStatus);
    }

    [Fact]
    public async Task RunAsync_UploadWithAnotherIsbn_IsLeftToTheTitleMatch()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Der Weg der Koenige", "Brandon Sanderson", isbn: "9780306406157")));
        harness.Books.Books = [Book(1, "The Way of Kings", "Brandon Sanderson", "9780765326355")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Empty(harness.Context.SelectedTorrents);
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_BookWithoutIdentifiers_KeepsNone()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Null(Item(harness, 1).IdentifiersJson);
    }

    [Fact]
    public async Task RunAsync_FullFirstPageWithoutTheBook_SearchesTheSecondPageOnce()
    {
        var harness = CreateHarness(body => body.Contains("\"startNumber\":100", StringComparison.Ordinal)
            ? Results(Record(11, "Dune", "Frank Herbert"))
            : FullPageOfOtherBooks());
        harness.Books.Books = [Book(1, "Dune")];

        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(2, harness.Searches.Count);
        Assert.Equal(1, summary.Selected);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents).MamId);
    }

    [Fact]
    public async Task RunAsync_FullSecondPageWithoutTheBook_ReadsNoThirdPage()
    {
        var harness = CreateHarness(_ => FullPageOfOtherBooks());
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(2, harness.Searches.Count);
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_ShortFirstPageWithoutTheBook_ReadsNoSecondPage()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Something Else Entirely", "Someone Else")));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_FullFirstPageWithTheBook_ReadsNoSecondPage()
    {
        var harness = CreateHarness(_ => Results([.. Enumerable.Range(1, 99).Select(i => Record(1000 + i, $"Other Book {i}", "Someone Else")), Record(11, "Dune", "Frank Herbert")]));
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_BookMissingFromMamForAWeek_IsSearchedOnlyWeekly()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune")];
        var start = harness.Clock.Now;

        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(start, Item(harness, 1).MissingSince);

        harness.Clock.Now = start.AddDays(7);
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(2, harness.Searches.Count);

        harness.Clock.Now = start.AddDays(9);
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(2, harness.Searches.Count);

        harness.Clock.Now = start.AddDays(14);
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(3, harness.Searches.Count);
        Assert.Equal(start, Item(harness, 1).MissingSince);
    }

    [Fact]
    public async Task RunAsync_BookFoundAfterBeingMissing_IsNoLongerMissing()
    {
        var onMam = false;
        var harness = CreateHarness(_ => onMam ? Results(Record(11, "Dune", "Frank Herbert", free: false)) : EmptyResultJson);
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));
        onMam = true;
        harness.Clock.Now += TimeSpan.FromDays(1);
        await harness.RunAsync(Entry(grab: AudioOnly()));

        var item = Item(harness, 1);
        Assert.Equal(ListItemSideStatus.Wanted, item.AudioStatus);
        Assert.Null(item.MissingSince);
    }

    [Fact]
    public async Task RunAsync_SelectionRemovedFromTheQueue_IsSearchedForAndSelectedAgain()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry(grab: AudioOnly()));
        var selection = await harness.Context.SelectedTorrents.SingleAsync();
        selection.RemovedAt = harness.Clock.Now;
        await harness.Context.SaveChangesAsync();

        harness.Clock.Now += TimeSpan.FromHours(1);
        var summary = await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Equal(2, harness.Searches.Count);
        Assert.Equal(1, summary.Selected);
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 1).AudioStatus);
        Assert.Single(harness.Context.SelectedTorrents.AsNoTracking(), s => s.RemovedAt == null);
    }

    [Fact]
    public async Task RunAsync_SelectionThatWasDownloaded_StaysSelected()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry(grab: AudioOnly()));
        var selection = await harness.Context.SelectedTorrents.SingleAsync();
        selection.RemovedAt = harness.Clock.Now;
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "hash11",
            MamId = 11,
            NormalizedTitleKey = TitleNormalizer.Normalize("Dune"),
            Title = "Dune",
            MetadataJson = TorrentMetadataSerializer.Serialize(MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 11, title: "Dune", authorName: "Frank Herbert"))),
            CreatedAt = harness.Clock.Now,
        });
        await harness.Context.SaveChangesAsync();

        harness.Clock.Now += TimeSpan.FromDays(3);
        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_SelectedAsADuplicateOfAnotherSelection_IsNotSearchedAgain()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        var other = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 12, title: "Dune", authorName: "Frank Herbert"));
        harness.Context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 12,
            DownloadLink = "dl12",
            CostDecision = "Ratio",
            MetadataJson = TorrentMetadataSerializer.Serialize(other),
            NormalizedTitleKey = TitleNormalizer.Normalize("Dune"),
            CreatedAt = harness.Clock.Now,
        });
        await harness.Context.SaveChangesAsync();
        harness.Books.Books = [Book(1, "Dune")];

        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal((ListItemSideStatus.Selected, 11L), (Item(harness, 1).AudioStatus, Item(harness, 1).AudioMamId));

        harness.Clock.Now += TimeSpan.FromDays(3);
        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_LibraryCopyWhoseRecordWasRemoved_IsSearchedForAgain()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 5,
            NormalizedTitleKey = TitleNormalizer.Normalize("Dune"),
            Title = "Dune",
            MetadataJson = TorrentMetadataSerializer.Serialize(MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 5, title: "Dune", authorName: "Frank Herbert"))),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Context.SaveChangesAsync();
        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry(grab: AudioOnly()));
        Assert.Equal(ListItemSideStatus.Existing, Item(harness, 1).AudioStatus);

        harness.Context.Torrents.Remove(await harness.Context.Torrents.SingleAsync());
        await harness.Context.SaveChangesAsync();
        await harness.RunAsync(Entry(grab: AudioOnly()));

        Assert.Single(harness.Searches);
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_ListsOwnUnsatBuffer_LowersTheRunsLimit()
    {
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        var entry = Entry(grab: AudioOnly());
        entry.UnsatBuffer = 20; // the account has 20 slots, none in use

        var summary = await harness.RunAsync(entry);

        Assert.Equal((0, 0), (summary.Selected, summary.Searched));
        Assert.Empty(harness.Searches);
    }

    [Fact]
    public async Task RunAsync_ListsMaxActiveDownloads_StopsSelectingOnceThatManyWait()
    {
        var harness = CreateHarness(body => body.Contains("Messiah", StringComparison.Ordinal)
            ? Results(Record(12, "Dune Messiah", "Frank Herbert"))
            : Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune"), Book(2, "Dune Messiah")];
        var entry = Entry(grab: AudioOnly());
        entry.MaxActiveDownloads = 1;

        var summary = await harness.RunAsync(entry);

        Assert.Equal(1, summary.Selected);
        var selected = Assert.Single(harness.Context.SelectedTorrents);
        Assert.Equal((11L, "Want to Read"), (selected.MamId, selected.GrabberName));

        // The next run finds its one selection still waiting, so it selects nothing more.
        harness.Clock.Now += TimeSpan.FromDays(1);
        Assert.Equal(0, (await harness.RunAsync(entry)).Selected);
    }

    [Fact]
    public async Task RunAsync_BookInBothFormats_WithRoomForOneSelection_LeavesTheEbookForTheNextRun()
    {
        // Found by verifying the cap on the live list: one book in both formats made two selections under a cap of one.
        var harness = CreateHarness(_ => Results(Record(11, "Dune", "Frank Herbert"), Record(12, "Dune", "Frank Herbert", ebook: true)));
        harness.Books.Books = [Book(1, "Dune")];
        var entry = Entry();
        entry.MaxActiveDownloads = 1;

        var summary = await harness.RunAsync(entry);

        Assert.Equal(1, summary.Selected);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents).MamId);
        var item = Item(harness, 1);
        Assert.Equal((ListItemSideStatus.Selected, (string?)null, (DateTimeOffset?)null), (item.AudioStatus, item.EbookStatus, item.LastSearchedAt));

        // Still one waiting: the next run has no room, and searches nothing.
        await harness.RunAsync(entry);
        Assert.Single(harness.Searches);

        // The audiobook started downloading, so the cap has room again: the ebook is selected without waiting a day.
        var audiobook = await harness.Context.SelectedTorrents.SingleAsync();
        audiobook.StartedAt = harness.Clock.Now;
        await harness.Context.SaveChangesAsync();
        harness.Clock.Now += TimeSpan.FromMinutes(30);
        Assert.Equal(1, (await harness.RunAsync(entry)).Selected);
        Assert.Equal([11L, 12L], harness.Context.SelectedTorrents.AsNoTracking().Select(t => t.MamId).Order());
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 1).EbookStatus);
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
    public async Task RunAsync_PreferredFormatAlreadySelected_OtherFormatFoundLaterIsNotWanted()
    {
        var ebookOnMam = false;
        var harness = CreateHarness(_ => ebookOnMam
            ? Results(Record(11, "Dune", "Frank Herbert"), Record(12, "Dune", "Frank Herbert", ebook: true))
            : Results(Record(11, "Dune", "Frank Herbert")));
        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry(preferFormat: FilterMainType.Audio));
        Assert.Equal(ListItemSideStatus.NotFound, Item(harness, 1).EbookStatus);

        ebookOnMam = true;
        harness.Clock.Now += TimeSpan.FromDays(1);
        var summary = await harness.RunAsync(Entry(preferFormat: FilterMainType.Audio));

        var item = Item(harness, 1);
        Assert.Equal(0, summary.Selected);
        Assert.Equal(ListItemSideStatus.Selected, item.AudioStatus);
        Assert.Equal(ListItemSideStatus.NotWanted, item.EbookStatus);
        Assert.Single(harness.Searches);
        Assert.Equal(11, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking()).MamId);
    }

    [Fact]
    public async Task RunAsync_PreferredFormatSelectionRemoved_OtherFormatIsWantedAgain()
    {
        var audioOnMam = true;
        var harness = CreateHarness(_ => audioOnMam
            ? Results(Record(11, "Dune", "Frank Herbert"), Record(12, "Dune", "Frank Herbert", ebook: true))
            : Results(Record(12, "Dune", "Frank Herbert", ebook: true)));
        harness.Books.Books = [Book(1, "Dune")];
        await harness.RunAsync(Entry(preferFormat: FilterMainType.Audio));
        var selection = await harness.Context.SelectedTorrents.SingleAsync();
        selection.RemovedAt = harness.Clock.Now;
        await harness.Context.SaveChangesAsync();

        audioOnMam = false;
        harness.Clock.Now += TimeSpan.FromHours(1);
        var summary = await harness.RunAsync(Entry(preferFormat: FilterMainType.Audio));

        var item = Item(harness, 1);
        Assert.Equal(1, summary.Selected);
        Assert.Equal(ListItemSideStatus.NotFound, item.AudioStatus);
        Assert.Equal(ListItemSideStatus.Selected, item.EbookStatus);
        Assert.Equal(12, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking(), s => s.RemovedAt == null).MamId);
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

    private FileListEntry FileEntry(string text)
    {
        var path = Path.Combine(_root, "wanted.csv");
        File.WriteAllText(path, text);
        return new FileListEntry { Name = "Goodreads", Path = "wanted.csv", FullPath = path, Grab = [AudioOnly()] };
    }

    [Fact]
    public async Task RunAsync_FileList_StoresItsBooksAndSelects_LikeAHardcoverList()
    {
        var harness = CreateHarness(body => body.Contains("Dune", StringComparison.Ordinal) ? Results(Record(11, "Dune", "Frank Herbert")) : EmptyResultJson);
        var entry = FileEntry("Title,Author,ISBN13\nDune,Frank Herbert,9780441172719\nHyperion,Dan Simmons,\n,Nobody,\n");

        var summary = await harness.RunAsync(ListDefinition.ForFile(entry, 0), new FileListSource(entry));

        Assert.Equal((1, 2, 2, 1), (summary.Selected, summary.Books, summary.Searched, summary.UnreadableLines));
        var list = Assert.Single(harness.Context.Lists);
        Assert.Equal(("file:Goodreads", "Goodreads", "File"), (list.Id, list.Name, list.ListType));
        var items = harness.Context.ListItems.AsNoTracking().OrderBy(i => i.Position).ToList();
        Assert.Equal(["isbn:9780441172719", "title:hyperion|dansimmons"], items.Select(i => i.ExternalId));
        Assert.Equal((ListItemSideStatus.Selected, 11L), (items[0].AudioStatus, items[0].AudioMamId!.Value));
        Assert.Equal(ListItemSideStatus.NotFound, items[1].AudioStatus);
        Assert.Null(items[0].CoverUrl);
        Assert.Null(items[0].BookUrl);
        Assert.Equal("Goodreads", Assert.Single(harness.Context.SelectedTorrents).GrabberName);
    }

    [Fact]
    public async Task RunAsync_FileListBookFails_IsRecordedUnderTheFileListStage()
    {
        var harness = CreateHarness(_ => "not json");
        var entry = FileEntry("Dune by Frank Herbert\n");

        await harness.RunAsync(ListDefinition.ForFile(entry, 0), new FileListSource(entry));

        var error = Assert.Single(harness.Context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.FileList, "file:Goodreads title:dune|frankherbert"), (error.Stage, error.Key));
    }

    [Fact]
    public async Task RunAsync_FileListWhoseFileIsMissing_FailsTheRun_WithoutAskingMam()
    {
        var harness = CreateHarness(_ => EmptyResultJson);
        var entry = new FileListEntry { Path = "lists/gone.csv", FullPath = Path.Combine(_root, "lists", "gone.csv"), Grab = [AudioOnly()] };

        var error = await Assert.ThrowsAsync<ListFileException>(() => harness.RunAsync(ListDefinition.ForFile(entry, 0), new FileListSource(entry)));

        Assert.Equal("List file not found: lists/gone.csv", error.Message);
        Assert.Empty(harness.Searches);
        Assert.Empty(harness.Context.Lists);
    }

    private static bool IsBookmarksSearch(string body) => body.Contains("\"searchIn\":\"bookmarks\"", StringComparison.Ordinal);

    private static readonly ListDefinition BookmarksList = ListDefinition.ForBookmarks(new BookmarksListEntry { Grab = [new GrabEntry { Cost = ConfiguredCost.Free }] });

    [Fact]
    public async Task RunAsync_Bookmark_IsSelected_WithoutSearchingForIt()
    {
        var harness = CreateHarness(body => IsBookmarksSearch(body) ? Results(Record(11, "Dune", "Frank Herbert")) : EmptyResultJson);

        var summary = await harness.RunAsync(BookmarksList, harness.Bookmarks);

        Assert.Equal((1, 1, 1, true), (summary.Selected, summary.Books, summary.Searched, summary.Bookmarks));
        Assert.Single(harness.Searches);
        var list = Assert.Single(harness.Context.Lists);
        Assert.Equal(("mam:bookmarks", "bookmarks_list", "MAM bookmarks"), (list.Id, list.Name, list.ListType));
        var item = Item(harness, 11);
        Assert.Equal((ListItemSideStatus.Selected, 11L), (item.AudioStatus, item.AudioMamId!.Value));
        Assert.True(item.AllowAudio);
        Assert.False(item.AllowEbook);
        Assert.Equal("bookmarks_list", Assert.Single(harness.Context.SelectedTorrents).GrabberName);
    }

    [Fact]
    public async Task RunAsync_BookmarkCostingMore_IsWanted_AndTakenOnTheNextRunOnceItIsFree()
    {
        var free = false;
        var harness = CreateHarness(body => IsBookmarksSearch(body) ? Results(Record(11, "Dune", "Frank Herbert", free: free)) : EmptyResultJson);

        await harness.RunAsync(BookmarksList, harness.Bookmarks);
        Assert.Equal(ListItemSideStatus.Wanted, Item(harness, 11).AudioStatus);

        free = true;
        var summary = await harness.RunAsync(BookmarksList, harness.Bookmarks);

        Assert.Equal(1, summary.Selected);
        Assert.Equal(ListItemSideStatus.Selected, Item(harness, 11).AudioStatus);
    }

    [Fact]
    public async Task RunAsync_BookmarkNoGrabBlockTakes_SaysSo()
    {
        var harness = CreateHarness(body => IsBookmarksSearch(body) ? Results(Record(11, "Dune", "Frank Herbert")) : EmptyResultJson);
        var german = ListDefinition.ForBookmarks(new BookmarksListEntry { Grab = [new GrabEntry { Cost = ConfiguredCost.Free, Languages = ["German"] }] });

        var summary = await harness.RunAsync(german, harness.Bookmarks);

        Assert.Equal(0, summary.Selected);
        Assert.Equal((ListItemSideStatus.NoGrabBlock, 11L), (Item(harness, 11).AudioStatus, Item(harness, 11).AudioMamId!.Value));
        Assert.Empty(harness.Context.SelectedTorrents);
    }

    [Fact]
    public async Task RunAsync_EbookBookmark_IsLookedForAsAnEbookOnly()
    {
        var harness = CreateHarness(body => IsBookmarksSearch(body) ? Results(Record(12, "Hyperion", "Dan Simmons", ebook: true)) : EmptyResultJson);

        await harness.RunAsync(BookmarksList, harness.Bookmarks);

        var item = Item(harness, 12);
        Assert.Equal((false, true, ListItemSideStatus.Selected), (item.AllowAudio, item.AllowEbook, item.EbookStatus));
    }

    [Fact]
    public async Task RunAsync_BookmarkRemovedOnMam_LeavesTheList()
    {
        var bookmarked = true;
        var harness = CreateHarness(body => IsBookmarksSearch(body) && bookmarked ? Results(Record(11, "Dune", "Frank Herbert", free: false)) : EmptyResultJson);
        await harness.RunAsync(BookmarksList, harness.Bookmarks);

        bookmarked = false;
        await harness.RunAsync(BookmarksList, harness.Bookmarks);

        Assert.NotNull(Item(harness, 11).RemovedAt);
    }
}

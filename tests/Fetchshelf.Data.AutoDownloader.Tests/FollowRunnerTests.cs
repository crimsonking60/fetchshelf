using System.Net;
using System.Runtime.CompilerServices;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class FollowRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-follow-runner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static string AccountStateJson(int unsatLimit) => $$"""
        {
            "ratio": 5.0, "uploaded_bytes": 1000, "downloaded_bytes": 100, "wedges": 10, "uid": 1,
            "snatch_summary": { "unsat": { "count": 0, "limit": {{unsatLimit}} } }
        }
        """;

    /// <summary>A MAM search result: a book by <paramref name="author"/>, in <paramref name="series"/> when given.</summary>
    private static string Record(
        long mamId, string title, string author = "James S. A. Corey", string? series = null, string? number = null,
        bool ebook = false, bool free = true, string added = "2026-01-01 00:00:00", int? mediaType = null) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{{(series is null ? "{}" : $"{{\\\"7\\\":[\\\"{series}\\\",\\\"{number}\\\"]}}")}}",
            "isbn": null, "description": null,
            "mediatype": {{mediaType ?? (ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1, "category": {{(ebook ? 60 : 39)}}, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "{{(ebook ? "epub" : "m4b")}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "{{added}}",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
            "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private static string Results(params string[] records) =>
        $$"""{"perpage":100,"start":0,"total":{{records.Length}},"found":{{records.Length}},"data":[{{string.Join(",", records)}}]}""";

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Harness(FetchshelfDbContext context, List<string> searches, Func<FollowSettings, FollowRunner> create)
    {
        public FetchshelfDbContext Context => context;

        /// <summary>The body of every MAM search, in order.</summary>
        public List<string> Searches => searches;

        public Task<FollowRunSummary> RunAsync(FollowSettings? settings = null) =>
            create(settings ?? Settings()).RunAsync(ignoreTorrents: []);
    }

    /// <summary>A harness whose MAM answers each search with <paramref name="searchResponse"/>, given the search's body.</summary>
    private Harness CreateHarness(
        Func<string, HttpResponseMessage> searchResponse, int unsatLimit = 20, ListLogger<FollowRunner>? logger = null, FollowRunnerOptions? options = null)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var searches = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson(unsatLimit)) };
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
        return new Harness(context, searches, settings => new FollowRunner(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            new LibraryQueries(context), settings, SelectionTestSupport.PreferenceLists, new Clock(Now),
            delay: (_, _) => Task.CompletedTask, logger: logger, options: options));
    }

    /// <summary>Makes every follow due for a search again, as it is a day later.</summary>
    private static async Task MakeDueAsync(Harness harness)
    {
        foreach (var follow in harness.Context.Follows)
        {
            follow.LastSearchedAt = Now.AddDays(-2);
        }

        await harness.Context.SaveChangesAsync();
    }

    /// <summary>A harness whose search answers with what <c>answer</c> holds at the time, so runs can see different results.</summary>
    private Harness SwitchingHarness(StrongBox<string> answer, FollowRunnerOptions? options = null) =>
        CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.Value!) }, options: options);

    private Harness CreateHarness(string results, int unsatLimit = 20, ListLogger<FollowRunner>? logger = null) =>
        CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(results) }, unsatLimit, logger);

    private static FollowSettings Settings(
        ConfiguredCost cost = ConfiguredCost.Free, FilterMainType? preferFormat = null, bool dryRun = false, int searchesPerRun = 10) => new()
        {
            Grab = [new GrabEntry { Cost = cost }],
            PreferFormat = preferFormat,
            DryRun = dryRun,
            SearchesPerRun = searchesPerRun,
            Category = "audiobooks",
        };

    private static FollowEntity Follow(FollowKind kind, string name, FollowMode? mode = null, DateTimeOffset? lastSearched = null) => new()
    {
        Kind = kind,
        NameKey = kind == FollowKind.Series ? SeriesNameKey.Of(name) : PersonNameKey.Of(name),
        DisplayName = name,
        Mode = mode ?? (kind == FollowKind.Series ? FollowMode.Download : FollowMode.Watch),
        CreatedAt = Now.AddDays(-10),
        LastSearchedAt = lastSearched,
    };

    private static void AddFollows(Harness harness, params FollowEntity[] follows)
    {
        harness.Context.Follows.AddRange(follows);
        harness.Context.SaveChanges();
    }

    /// <summary>An audiobook of the series in the library, with entry <paramref name="number"/>.</summary>
    private static void AddToLibrary(Harness harness, string id, long mamId, string title, string series, string number)
    {
        var metadata = new TorrentMetadata
        {
            MamId = mamId,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = title,
            Authors = ["James S. A. Corey"],
            Series = [SeriesParser.Parse(series, number)],
            FileTypes = ["m4b"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = id,
            MamId = mamId,
            Title = title,
            NormalizedTitleKey = TitleNormalizer.Normalize(title),
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UnixEpoch,
            LibraryPath = "/library/" + title,
        });
        harness.Context.SaveChanges();
    }

    private List<FollowFindEntity> Finds(Harness harness) => harness.Context.FollowFinds.AsNoTracking().OrderBy(f => f.MamId).ToList();

    [Fact]
    public async Task SeriesFollow_SelectsTheMissingEntry_AndLeavesTheOwnedOne()
    {
        var harness = CreateHarness(Results(
            Record(1, "Leviathan Wakes", series: "The Expanse", number: "1"),
            Record(2, "Caliban's War", series: "The Expanse", number: "2")));
        AddToLibrary(harness, "hash1", 100, "Leviathan Wakes (another upload)", "The Expanse", "1");
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 1, 0), (summary.Searched, summary.Selected, summary.NewFinds));
        var selected = Assert.Single(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Equal((2L, "audiobooks", "Follow: The Expanse"), (selected.MamId, selected.Category, selected.GrabberName));
        // The owned entry is listed as already owned, so the follow's page shows the whole series.
        Assert.Equal([(1L, FollowFindStatus.Owned), (2L, FollowFindStatus.Selected)], Finds(harness).Select(f => (f.MamId, f.Status)));
        var find = Finds(harness).Single(f => f.MamId == 2);
        Assert.Equal((2L, FollowFindStatus.Selected, "2", "Audio"), (find.MamId, find.Status, find.SeriesDisplay, find.MediaSide));
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single().LastSearchedAt);
        var search = Assert.Single(harness.Searches);
        Assert.Contains("\"srchIn\":[\"series\"]", search);
        Assert.Contains("\"sortType\":\"dateDesc\"", search);
        Assert.Contains("\"text\":\"The Expanse\"", search);
    }

    [Fact]
    public async Task NoFreeSlots_StillSearchesAndSelects_TheDownloadJobWaitsForASlot()
    {
        // The account has 20 slots and the buffer takes them all: an autograb would select nothing, a follow selects its
        // missing books, which wait on the Selected page until the Download job finds a slot beyond the buffer.
        var harness = CreateHarness(Results(Record(2, "Caliban's War", series: "The Expanse", number: "2")));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));
        var settings = Settings();
        settings.UnsatBuffer = 20;

        var summary = await harness.RunAsync(settings);

        Assert.Equal((1, 1), (summary.Searched, summary.Selected));
        Assert.Equal(2L, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking()).MamId);
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single().LastSearchedAt);
    }

    [Fact]
    public async Task ItsMaxActiveDownloads_CountsTheWaitingSelectionsOfEveryFollow()
    {
        var harness = CreateHarness(Results(Record(2, "Caliban's War", series: "The Expanse", number: "2")));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));
        harness.Context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 99,
            DownloadLink = "dl99",
            CostDecision = "GlobalFreeleech",
            MetadataJson = TorrentMetadataSerializer.Serialize(MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 99, title: "The Colour of Magic", authorName: "Terry Pratchett"))),
            NormalizedTitleKey = "colour of magic",
            GrabberName = "Follow: Discworld",
            CreatedAt = Now,
        });
        harness.Context.SaveChanges();
        var settings = Settings();
        settings.MaxActiveDownloads = 1;

        var summary = await harness.RunAsync(settings);

        Assert.Equal(0, summary.Selected);
        Assert.Single(harness.Context.SelectedTorrents.AsNoTracking());
    }

    [Fact]
    public async Task SeriesFollow_LogsWhereTheResultsWent_SuchAsToAnOwnedCollection()
    {
        var logger = new ListLogger<FollowRunner>();
        var harness = CreateHarness(
            Results(
                Record(1, "Leviathan Wakes", series: "The Expanse", number: "1"),
                Record(2, "Caliban's War", series: "The Expanse", number: "2"),
                Record(3, "Caliban's War", series: "The Expanse", number: "2"),
                Record(4, "Abaddon's Gate", series: "The Expanse", number: "3"),
                Record(5, "Cibola Burn", series: "The Expanse", number: "4"),
                Record(6, "Nemesis Games", series: "The Expanse", number: "5"),
                Record(7, "Nemesis Games", series: "The Expanse", number: "5"),
                Record(8, "Persepolis Rising", series: "Some Other Series", number: "7")),
            logger: logger);
        // A collection of the first three books: every upload of one of them is owned.
        AddToLibrary(harness, "hash1", 100, "The Expanse Books 1-3", "The Expanse", "1-3");
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));

        await harness.RunAsync();

        Assert.Contains(
            "The Expanse: 8 torrents on MAM in 1 page, 7 of them ebooks or audiobooks in the series; 4 already in the library or selected; "
            + "the other 3 are 2 books (one torrent per book, language and format). Of those, 0 found before, 2 new to look at, 0 selected, 0 not free",
            logger.Messages(LogLevel.Information));
        Assert.Equal([5, 6], Finds(harness).Where(f => f.Status == FollowFindStatus.New).Select(f => f.MamId));
        // The owned books 1-3 are listed too, one find per book: book 2 has two uploads.
        Assert.Equal(["1", "2", "3"], Finds(harness).Where(f => f.Status == FollowFindStatus.Owned).Select(f => f.SeriesDisplay).Order());
    }

    [Fact]
    public async Task AWaitingFind_IsOwnedOnceTheLibraryCoversIt_ThoughNoSearchReturnsItAgain()
    {
        // An old upload found before a collection brought its book in, and no longer among the newest uploads a search reads.
        var answer = new StrongBox<string>(Results(Record(10, "Caliban's War", series: "The Expanse", number: "2")));
        var harness = SwitchingHarness(answer);
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));
        await harness.RunAsync();
        Assert.Equal(FollowFindStatus.New, Finds(harness).Single().Status);

        AddToLibrary(harness, "hash1", 100, "The Expanse Books 1-3", "The Expanse", "1-3");
        answer.Value = Results();
        await MakeDueAsync(harness);
        await harness.RunAsync();

        Assert.Equal((10L, FollowFindStatus.Owned), Finds(harness).Select(f => (f.MamId, f.Status)).Single());
    }

    [Theory]
    [InlineData(Language.English, FollowFindStatus.Owned)]
    [InlineData(Language.Hungarian, FollowFindStatus.New)]
    public async Task AFindRecordedWithoutALanguage_IsCheckedAsTheFirstConfiguredOne(Language first, FollowFindStatus expected)
    {
        var answer = new StrongBox<string>(Results(Record(10, "Caliban's War", series: "The Expanse", number: "2")));
        var harness = SwitchingHarness(answer, new FollowRunnerOptions(first));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));
        await harness.RunAsync();
        // As one recorded before languages were kept.
        harness.Context.FollowFinds.Single().Language = null;
        await harness.Context.SaveChangesAsync();

        AddToLibrary(harness, "hash1", 100, "The Expanse Books 1-3", "The Expanse", "1-3"); // in English
        answer.Value = Results();
        await MakeDueAsync(harness);
        await harness.RunAsync();

        Assert.Equal(expected, Finds(harness).Single().Status);
    }

    [Fact]
    public async Task ANewerUploadOfABookAlreadyListed_IsNoSecondFind()
    {
        var answer = new StrongBox<string>(Results(Record(10, "Caliban's War", series: "The Expanse", number: "2")));
        var harness = SwitchingHarness(answer);
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));
        await harness.RunAsync();

        answer.Value = Results(Record(11, "Caliban's War", series: "The Expanse", number: "2", added: "2026-02-01 00:00:00"));
        await MakeDueAsync(harness);
        await harness.RunAsync();

        Assert.Equal([(10L, FollowFindStatus.New)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task FollowsNotDue_HaveFindsThatReachedTheLibraryRetired_WithoutASearch()
    {
        var harness = CreateHarness(Results());
        // Both searched just now, so neither is due; the ignored one is left alone.
        AddFollows(harness,
            Follow(FollowKind.Author, "James S. A. Corey", FollowMode.Watch, lastSearched: Now),
            Follow(FollowKind.Series, "The Expanse", FollowMode.Ignore, lastSearched: Now));
        var (watched, ignored) = (harness.Context.Follows.Single(f => f.Mode == FollowMode.Watch).Id, harness.Context.Follows.Single(f => f.Mode == FollowMode.Ignore).Id);
        FollowFindEntity Find(int followId, long mamId, string title) => new()
        {
            FollowId = followId,
            MamId = mamId,
            Title = title,
            MediaSide = "Audio",
            Language = "English",
            Status = FollowFindStatus.New,
            FoundAt = Now.AddHours(-5),
            StatusAt = Now.AddHours(-5),
        };
        harness.Context.FollowFinds.AddRange(
            Find(watched, 1, "Leviathan Wakes"), Find(watched, 2, "Caliban's War"), Find(ignored, 3, "Leviathan Wakes"));
        await harness.Context.SaveChangesAsync();
        // Grabbed since by another route, such as a series follow on Auto.
        AddToLibrary(harness, "hash1", 1, "Leviathan Wakes", "The Expanse", "1");
        AddToLibrary(harness, "hash3", 3, "Leviathan Wakes", "The Expanse", "1");

        var summary = await harness.RunAsync();

        Assert.Equal(0, summary.Searched);
        Assert.Empty(harness.Searches);
        Assert.Equal([(1L, FollowFindStatus.Owned), (2L, FollowFindStatus.New), (3L, FollowFindStatus.New)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task AFind_KeepsItsMediaType_AsWellAsItsSide()
    {
        var harness = CreateHarness(Results(
            Record(1, "Leviathan Wakes"),
            Record(2, "The Expanse Graphic Novel", ebook: true, mediaType: 6)));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        await harness.RunAsync();

        Assert.Equal([("Audio", "Audiobook"), ("Ebook", "ComicBook")], Finds(harness).Select(f => (f.MediaSide, f.MediaType!)));
    }

    [Fact]
    public async Task AFindRecordedBeforeMediaTypesWereKept_GetsItsMediaTypeWhenFoundAgain()
    {
        var harness = CreateHarness(Results(Record(2, "The Expanse Graphic Novel", ebook: true, mediaType: 6)));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));
        harness.Context.FollowFinds.Add(new FollowFindEntity
        {
            FollowId = harness.Context.Follows.Single().Id,
            MamId = 2,
            Title = "The Expanse Graphic Novel",
            MediaSide = "Ebook",
            Language = "English",
            Status = FollowFindStatus.New,
            FoundAt = Now.AddDays(-5),
            StatusAt = Now.AddDays(-5),
        });
        await harness.Context.SaveChangesAsync();

        await harness.RunAsync();

        Assert.Equal("ComicBook", Assert.Single(Finds(harness)).MediaType);
    }

    /// <summary>The settings with one free grab block that takes only uploads from June 2026 on.</summary>
    private static FollowSettings NarrowedSettings()
    {
        var settings = Settings();
        settings.Grab = [new GrabEntry { Cost = ConfiguredCost.Free, UploadedAfter = "2026-06-01" }];
        return settings;
    }

    [Fact]
    public async Task ANarrowedGrabBlock_RemovesTheWaitingFindsItNoLongerTakes_KeepingTheDismissedOnes()
    {
        var answer = new StrongBox<string>(Results(
            Record(1, "Old Book", added: "2026-01-01 00:00:00"),
            Record(2, "Old Dismissed Book", added: "2026-01-02 00:00:00"),
            Record(3, "New Book", added: "2026-07-01 00:00:00")));
        var harness = SwitchingHarness(answer);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));
        await harness.RunAsync();
        harness.Context.FollowFinds.Single(f => f.MamId == 2).Status = FollowFindStatus.Dismissed;
        await harness.Context.SaveChangesAsync();
        await MakeDueAsync(harness);

        // Not a read of every page: only the finds whose torrent the search returned are looked at.
        await harness.RunAsync(NarrowedSettings());

        Assert.Equal([(2L, FollowFindStatus.Dismissed), (3L, FollowFindStatus.New)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task AReadOfEveryPage_RemovesTheWaitingFindsItDidNotReturn_ALaterSearchKeepsThem()
    {
        var answer = new StrongBox<string>(Results(Record(1, "Leviathan Wakes"), Record(2, "Caliban's War")));
        var harness = SwitchingHarness(answer);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));
        await harness.RunAsync();

        // A later search that doesn't read every page doesn't know about torrents it didn't return.
        answer.Value = Results(Record(2, "Caliban's War"));
        await MakeDueAsync(harness);
        await harness.RunAsync();
        Assert.Equal([1L, 2L], Finds(harness).Select(f => f.MamId));

        // Search now: every page is read, and the first book is no longer among them.
        harness.Context.Follows.Single().LastSearchedAt = null;
        await harness.Context.SaveChangesAsync();
        await harness.RunAsync();
        Assert.Equal([2L], Finds(harness).Select(f => f.MamId));
    }

    [Fact]
    public async Task AReadOfEveryPageThatReturnsNothing_RemovesNoFinds()
    {
        var answer = new StrongBox<string>(Results(Record(1, "Leviathan Wakes")));
        var harness = SwitchingHarness(answer);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));
        await harness.RunAsync();

        answer.Value = Results();
        harness.Context.Follows.Single().LastSearchedAt = null;
        await harness.Context.SaveChangesAsync();
        await harness.RunAsync();

        Assert.Equal([1L], Finds(harness).Select(f => f.MamId));
    }

    [Fact]
    public async Task FindsOfOneBookAndFormat_AreTidiedToTheMostSettled_LeavingTheDismissedOne()
    {
        var harness = CreateHarness(Results());
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));
        var followId = harness.Context.Follows.Single().Id;
        FollowFindEntity Find(long mamId, FollowFindStatus status, int day) => new()
        {
            FollowId = followId,
            MamId = mamId,
            Title = "Caliban's War",
            SeriesDisplay = "2",
            MediaSide = "Audio",
            Language = "English",
            Status = status,
            FoundAt = Now.AddDays(-day),
            StatusAt = Now.AddDays(-day),
        };
        harness.Context.FollowFinds.AddRange(
            Find(10, FollowFindStatus.New, 9), Find(11, FollowFindStatus.Owned, 5), Find(12, FollowFindStatus.Owned, 2), Find(13, FollowFindStatus.Dismissed, 1));
        await harness.Context.SaveChangesAsync();

        await harness.RunAsync();

        // The first found of the owned ones stays, and the dismissed one, the reader's choice.
        Assert.Equal([(11L, FollowFindStatus.Owned), (13L, FollowFindStatus.Dismissed)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task SeriesFollow_AlreadyHadBooks_AreListedOnce_AndASecondRunAddsNone()
    {
        var harness = CreateHarness(Results(
            Record(1, "Leviathan Wakes", series: "The Expanse", number: "1"),
            Record(2, "Leviathan Wakes", series: "The Expanse", number: "1"),
            Record(3, "Caliban's War", series: "The Expanse", number: "2")));
        AddToLibrary(harness, "hash1", 2, "Leviathan Wakes", "The Expanse", "1");
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));

        await harness.RunAsync();
        await harness.RunAsync();

        // Of the two uploads of book 1, the one in the library stands for it.
        Assert.Equal([(2L, FollowFindStatus.Owned), (3L, FollowFindStatus.New)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task SeriesFollow_ABookWithAFindAlready_IsLeftAsItIs()
    {
        var harness = CreateHarness(Results(
            Record(1, "Leviathan Wakes", series: "The Expanse", number: "1"),
            Record(2, "Leviathan Wakes", series: "The Expanse", number: "1")));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse", FollowMode.Watch));
        await harness.RunAsync();
        var first = Finds(harness).Single();
        harness.Context.FollowFinds.Single(f => f.Id == first.Id).Status = FollowFindStatus.Dismissed;
        await harness.Context.SaveChangesAsync();
        AddToLibrary(harness, "hash1", first.MamId == 1 ? 2 : 1, "Leviathan Wakes", "The Expanse", "1");

        await harness.RunAsync();

        // Book 1 is in the library by now, through the other upload: the dismissed find stays, and no second one is added.
        Assert.Equal([(first.MamId, FollowFindStatus.Dismissed)], Finds(harness).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task SeriesFollow_AResultWhoseSeriesOnlySharesAWord_IsNotAFind()
    {
        var harness = CreateHarness(Results(Record(1, "The Butlerian Jihad", "Brian Herbert", series: "Legends of Dune", number: "1")));
        AddFollows(harness, Follow(FollowKind.Series, "Dune"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 0), (summary.Searched, summary.Selected));
        Assert.Empty(Finds(harness));
    }

    [Fact]
    public async Task WatchedAuthor_ListsANewFind_AndSelectsNothing()
    {
        var harness = CreateHarness(Results(Record(5, "Tiamat's Wrath"), Record(6, "Someone Else's Book", "Someone Else")));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        var summary = await harness.RunAsync();

        Assert.Equal((0, 1), (summary.Selected, summary.NewFinds));
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
        var find = Assert.Single(Finds(harness));
        Assert.Equal((5L, FollowFindStatus.New, null, (long?)(100L * 1024 * 1024)), (find.MamId, find.Status, find.SeriesDisplay, find.SizeBytes));
        Assert.Contains("\"srchIn\":[\"author\"]", Assert.Single(harness.Searches));

        // Searched again later, a find already listed is not listed twice.
        harness.Context.Follows.Single().LastSearchedAt = null;
        harness.Context.SaveChanges();
        Assert.Equal(0, (await harness.RunAsync()).NewFinds);
        Assert.Single(Finds(harness));
    }

    [Fact]
    public async Task AFindAlreadyListedWithoutASize_GetsItWhenSearchedAgain()
    {
        var harness = CreateHarness(Results(Record(5, "Tiamat's Wrath")));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));
        await harness.RunAsync();

        // As a find listed before sizes were kept: it stays listed as it is, and only its size changes.
        var listed = harness.Context.FollowFinds.Single();
        (listed.SizeBytes, listed.StatusAt) = (null, DateTimeOffset.UnixEpoch);
        harness.Context.Follows.Single().LastSearchedAt = null;
        harness.Context.SaveChanges();
        await harness.RunAsync();

        var find = Assert.Single(Finds(harness));
        Assert.Equal((FollowFindStatus.New, DateTimeOffset.UnixEpoch, (long?)(100L * 1024 * 1024)), (find.Status, find.StatusAt, find.SizeBytes));
    }

    /// <summary>
    /// A MAM that has <paramref name="count"/> books by the author, newest first, and answers each search with the page
    /// its offset asks for. Book i was uploaded at <paramref name="addedAt"/>(i).
    /// </summary>
    /// <param name="count">The torrents MAM has for the search.</param>
    /// <param name="addedAt">Each one's upload time, by its place, newest first.</param>
    /// <param name="giveFound">Whether MAM gives the count, as found and total; else both are 0.</param>
    /// <param name="foundIsThePage">Whether found is the count of the page alone, and only total the whole count.</param>
    /// <param name="ignoreOffset">Whether MAM answers every page with the first.</param>
    private Harness CreatePagedHarness(
        int count, Func<int, DateTimeOffset> addedAt, bool giveFound = true, bool foundIsThePage = false, bool ignoreOffset = false,
        ListLogger<FollowRunner>? logger = null) =>
        CreateHarness(
            body =>
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                var start = ignoreOffset ? 0
                    : json.RootElement.TryGetProperty("tor", out var tor) && tor.TryGetProperty("startNumber", out var number) ? number.GetInt64() : 0;
                var page = Enumerable.Range((int)start, (int)Math.Clamp(count - start, 0, FollowRunner.PageSize))
                    .Select(i => Record(1000 + i, $"Book {i}", added: addedAt(i).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)))
                    .ToArray();
                var total = giveFound ? count : 0;
                var found = foundIsThePage ? page.Length : total;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"perpage":100,"start":{{start}},"total":{{total}},"found":{{found}},"data":[{{string.Join(",", page)}}]}"""),
                };
            },
            logger: logger);

    [Fact]
    public async Task AFirstSearch_ReadsEveryPage()
    {
        var harness = CreatePagedHarness(250, i => Now.AddHours(-i));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        var summary = await harness.RunAsync();

        Assert.Equal(3, harness.Searches.Count);
        Assert.Equal(250, summary.NewFinds);
        Assert.Equal(250, Finds(harness).Count);
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single().FullySearchedAt);
    }

    [Fact]
    public async Task WhenMamsFoundCountsOnlyThePage_TheTotalStillHasEveryPageRead()
    {
        var harness = CreatePagedHarness(250, i => Now.AddHours(-i), foundIsThePage: true);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        var summary = await harness.RunAsync();

        Assert.Equal(3, harness.Searches.Count);
        Assert.Equal(250, summary.NewFinds);
    }

    [Fact]
    public async Task WhenMamGivesTheFirstPageAgain_TheSearchStops_AndSaysSo()
    {
        var logger = new ListLogger<FollowRunner>();
        var harness = CreatePagedHarness(250, i => Now.AddHours(-i), ignoreOffset: true, logger: logger);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        var summary = await harness.RunAsync();

        Assert.Equal(2, harness.Searches.Count);
        Assert.Equal(100, summary.NewFinds);
        Assert.Null(harness.Context.Follows.AsNoTracking().Single().FullySearchedAt); // not every page
        Assert.Contains(logger.Messages(LogLevel.Warning), m => m.StartsWith(
            "James S. A. Corey: MAM gave page 2 (from torrent 100) only torrents an earlier page had, so the search stops at 100 torrents.",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALaterSearch_StopsAtTorrentsThePreviousSearchSaw()
    {
        // 150 books uploaded since the last search two days ago, then older ones: the second page reaches those.
        var harness = CreatePagedHarness(400, i => i < 150 ? Now.AddMinutes(-i) : Now.AddDays(-10).AddHours(-i));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey", lastSearched: Now.AddDays(-2)));
        harness.Context.Follows.Single().FullySearchedAt = Now.AddDays(-2);
        harness.Context.SaveChanges();

        await harness.RunAsync();

        Assert.Equal(2, harness.Searches.Count);
        Assert.Equal(Now.AddDays(-2), harness.Context.Follows.AsNoTracking().Single().FullySearchedAt); // not every page
    }

    [Fact]
    public async Task ASearchAfterReadAllAgainAfter_ReadsEveryPageAgain()
    {
        var harness = CreatePagedHarness(400, i => i < 150 ? Now.AddMinutes(-i) : Now.AddDays(-10).AddHours(-i));
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey", lastSearched: Now.AddDays(-2)));
        harness.Context.Follows.Single().FullySearchedAt = Now - FollowRunner.ReadAllAgainAfter;
        harness.Context.SaveChanges();

        await harness.RunAsync();

        Assert.Equal(4, harness.Searches.Count);
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single().FullySearchedAt);
    }

    [Fact]
    public async Task WithoutATotalFromMam_PagesAreReadUntilAShortOne()
    {
        var harness = CreatePagedHarness(230, i => Now.AddHours(-i), giveFound: false);
        AddFollows(harness, Follow(FollowKind.Author, "James S. A. Corey"));

        await harness.RunAsync();

        Assert.Equal(3, harness.Searches.Count);
        Assert.Equal(230, Finds(harness).Count);
    }

    [Fact]
    public async Task ACostNoGrabBlockAccepts_IsRecordedAsWanted()
    {
        var harness = CreateHarness(Results(Record(2, "Caliban's War", series: "The Expanse", number: "2", free: false)));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        var summary = await harness.RunAsync(Settings(ConfiguredCost.Free));

        Assert.Equal(0, summary.Selected);
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Equal(FollowFindStatus.Wanted, Assert.Single(Finds(harness)).Status);
    }

    [Fact]
    public async Task PreferFormat_ABookFoundInBothFormats_TakesOnlyThePreferredOne()
    {
        var harness = CreateHarness(Results(
            Record(2, "Caliban's War", series: "The Expanse", number: "2"),
            Record(3, "Caliban's War", series: "The Expanse", number: "2", ebook: true),
            Record(4, "Abaddon's Gate", series: "The Expanse", number: "3", ebook: true)));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        await harness.RunAsync(Settings(preferFormat: FilterMainType.Audio));

        // Book 2 as an audiobook only; book 3 exists only as an ebook, so that is taken.
        Assert.Equal([2L, 4L], harness.Context.SelectedTorrents.AsNoTracking().Select(s => s.MamId).OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task PreferFormat_WhenThePreferredFormatIsNotFree_TakesTheOtherOneThatIs()
    {
        var harness = CreateHarness(Results(
            Record(2, "Caliban's War", series: "The Expanse", number: "2"),
            Record(3, "Caliban's War", series: "The Expanse", number: "2", ebook: true, free: false)));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        await harness.RunAsync(Settings(preferFormat: FilterMainType.Ebook));

        Assert.Equal(2L, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking()).MamId);
        Assert.Equal(2L, Assert.Single(Finds(harness)).MamId);
    }

    [Fact]
    public async Task AnOmnibus_IsNotOwned_WhenOnlyItsFirstBookIs()
    {
        var harness = CreateHarness(Results(
            Record(3, "Abaddon's Gate", series: "The Expanse", number: "3"),
            Record(9, "The Expanse Books 3-5", series: "The Expanse", number: "3-5")));
        AddToLibrary(harness, "hash3", 300, "Abaddon's Gate (another upload)", "The Expanse", "3");
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        await harness.RunAsync();

        Assert.Equal(9L, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking()).MamId);
    }

    [Fact]
    public async Task ANameWithSearchSyntax_IsSearchedAsPlainWords()
    {
        var harness = CreateHarness(Results());
        AddFollows(harness, Follow(FollowKind.Series, "Ender's Saga / Shadow?"));

        await harness.RunAsync();

        Assert.Contains("\"text\":\"Ender s Saga Shadow\"", Assert.Single(harness.Searches));
    }

    [Fact]
    public async Task OnlyDueFollows_AreSearched_TheLongestWaitingFirst_UpToSearchesPerRun()
    {
        var harness = CreateHarness(Results());
        AddFollows(
            harness,
            Follow(FollowKind.Series, "Searched An Hour Ago", lastSearched: Now.AddHours(-1)),
            Follow(FollowKind.Series, "Searched Two Days Ago", lastSearched: Now.AddDays(-2)),
            Follow(FollowKind.Series, "Searched Three Days Ago", lastSearched: Now.AddDays(-3)),
            Follow(FollowKind.Series, "Ignored", FollowMode.Ignore));

        var summary = await harness.RunAsync(Settings(searchesPerRun: 1));

        Assert.Equal(1, summary.Searched);
        Assert.Contains("\"text\":\"Searched Three Days Ago\"", Assert.Single(harness.Searches));
    }

    [Fact]
    public async Task MaxActiveDownloads_StopsTheRun_AndTheFollowIsSearchedAgainNextRun()
    {
        var harness = CreateHarness(Results(
            Record(2, "Caliban's War", series: "The Expanse", number: "2"),
            Record(3, "Abaddon's Gate", series: "The Expanse", number: "3")));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));
        var settings = Settings();
        settings.MaxActiveDownloads = 1;

        var summary = await harness.RunAsync(settings);

        Assert.Equal(1, summary.Selected);
        Assert.Equal(2L, Assert.Single(harness.Context.SelectedTorrents.AsNoTracking()).MamId);
        Assert.Null(harness.Context.Follows.AsNoTracking().Single().LastSearchedAt);
    }

    [Fact]
    public async Task MamRefusingTheSession_FailsTheRun_WithoutAnErrorPerFollow()
    {
        var harness = CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        await Assert.ThrowsAsync<MamForbiddenException>(() => harness.RunAsync());

        Assert.Empty(harness.Context.ErroredTorrents.AsNoTracking());
    }

    [Fact]
    public async Task OneFollowFailing_IsRecordedOnTheErrorsPage_AndTheRunGoesOn()
    {
        var harness = CreateHarness(body => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.Contains("Broken", StringComparison.Ordinal) ? "not json" : Results(Record(5, "Tiamat's Wrath"))),
        });
        AddFollows(harness, Follow(FollowKind.Series, "Broken", lastSearched: Now.AddDays(-3)), Follow(FollowKind.Author, "James S. A. Corey"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 1), (summary.Failed, summary.NewFinds));
        var error = Assert.Single(harness.Context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.Follow, "Broken"), (error.Stage, error.Title));
        // Counted as searched, so it doesn't take a search from the others on every run.
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single(f => f.DisplayName == "Broken").LastSearchedAt);
    }

    [Fact]
    public async Task ADryRun_WritesNothing()
    {
        var harness = CreateHarness(Results(Record(2, "Caliban's War", series: "The Expanse", number: "2")));
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        var summary = await harness.RunAsync(Settings(dryRun: true));

        Assert.Equal((1, true), (summary.Selected, summary.DryRun));
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Empty(Finds(harness));
        Assert.Null(harness.Context.Follows.AsNoTracking().Single().LastSearchedAt);
    }
}

using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

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
        bool ebook = false, bool free = true, string added = "2026-01-01 00:00:00") => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{{(series is null ? "{}" : $"{{\\\"7\\\":[\\\"{series}\\\",\\\"{number}\\\"]}}")}}",
            "isbn": null, "description": null,
            "mediatype": {{(ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1, "category": {{(ebook ? 60 : 39)}}, "categories": "[]",
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
            create(settings ?? Settings()).RunAsync(unsatBuffer: 0, ignoreTorrents: []);
    }

    /// <summary>A harness whose MAM answers each search with <paramref name="searchResponse"/>, given the search's body.</summary>
    private Harness CreateHarness(Func<string, HttpResponseMessage> searchResponse, int unsatLimit = 20)
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
            delay: (_, _) => Task.CompletedTask));
    }

    private Harness CreateHarness(string results, int unsatLimit = 20) =>
        CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(results) }, unsatLimit);

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
        var find = Assert.Single(Finds(harness));
        Assert.Equal((2L, FollowFindStatus.Selected, "2", "Audio"), (find.MamId, find.Status, find.SeriesDisplay, find.MediaSide));
        Assert.Equal(Now, harness.Context.Follows.AsNoTracking().Single().LastSearchedAt);
        var search = Assert.Single(harness.Searches);
        Assert.Contains("\"srchIn\":[\"series\"]", search);
        Assert.Contains("\"sortType\":\"dateDesc\"", search);
        Assert.Contains("\"text\":\"The Expanse\"", search);
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
        Assert.Equal((5L, FollowFindStatus.New, null), (find.MamId, find.Status, find.SeriesDisplay));
        Assert.Contains("\"srchIn\":[\"author\"]", Assert.Single(harness.Searches));

        // Searched again later, a find already listed is not listed twice.
        harness.Context.Follows.Single().LastSearchedAt = null;
        harness.Context.SaveChanges();
        Assert.Equal(0, (await harness.RunAsync()).NewFinds);
        Assert.Single(Finds(harness));
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
    public async Task TheSelectionLimit_StopsTheRun_AndTheFollowIsSearchedAgainNextRun()
    {
        var harness = CreateHarness(Results(
            Record(2, "Caliban's War", series: "The Expanse", number: "2"),
            Record(3, "Abaddon's Gate", series: "The Expanse", number: "3")), unsatLimit: 1);
        AddFollows(harness, Follow(FollowKind.Series, "The Expanse"));

        var summary = await harness.RunAsync();

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

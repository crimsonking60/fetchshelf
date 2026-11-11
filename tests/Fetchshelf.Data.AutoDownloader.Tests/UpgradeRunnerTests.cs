using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class UpgradeRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-upgrade-runner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static string AccountStateJson(int unsatLimit) => $$"""
        {
            "ratio": 5.0, "uploaded_bytes": 1000, "downloaded_bytes": 100, "wedges": 10, "uid": 1,
            "snatch_summary": { "unsat": { "count": 0, "limit": {{unsatLimit}} } }
        }
        """;

    private static string Record(long mamId, string title = "Dune", string author = "Frank Herbert", string filetype = "m4b", bool free = true) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
            "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "{{filetype}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
            "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private static string Results(params string[] records) =>
        $$"""{"perpage":100,"start":0,"total":{{records.Length}},"found":{{records.Length}},"data":[{{string.Join(",", records)}}]}""";

    private static readonly IReadOnlyList<Library> Libraries =
    [
        new Library { Name = "Audiobooks", Category = "audiobooks", LibraryDir = "/library/audiobooks" },
        new Library { Name = "Other", Category = "other", LibraryDir = "/library/other" },
    ];

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Harness(FetchshelfDbContext context, List<string> searches, Func<UpgradeSettings, UpgradeRunner> create)
    {
        public FetchshelfDbContext Context => context;

        public List<string> Searches => searches;

        public Task<UpgradeRunSummary> RunAsync(UpgradeSettings? settings = null) =>
            create(settings ?? Settings()).RunAsync(unsatBuffer: 0, ignoreTorrents: []);
    }

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
        return new Harness(context, searches, settings => new UpgradeRunner(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            settings, Libraries, SelectionTestSupport.PreferenceLists, new Clock(Now), delay: (_, _) => Task.CompletedTask));
    }

    private Harness CreateHarness(string results, int unsatLimit = 20) =>
        CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(results) }, unsatLimit);

    private static UpgradeSettings Settings(bool dryRun = false, int searchesPerRun = 20, params string[] libraries) => new()
    {
        Grab = [new GrabEntry { Cost = ConfiguredCost.Free }],
        DryRun = dryRun,
        SearchesPerRun = searchesPerRun,
        Libraries = [.. libraries],
    };

    /// <summary>A linked mp3 copy in the Audiobooks library, which an m4b would beat.</summary>
    private static TorrentEntity Copy(
        string id, string title = "Dune", string format = "mp3", string library = "Audiobooks", string category = "audiobooks",
        DateTimeOffset? searchedAt = null)
    {
        var metadata = new TorrentMetadata
        {
            MamId = 100 + id.Length,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = title,
            Authors = ["Frank Herbert"],
            FileTypes = [format],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };
        return new TorrentEntity
        {
            Id = id,
            MamId = metadata.MamId,
            Title = title,
            NormalizedTitleKey = TitleNormalizer.Normalize(title),
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UnixEpoch,
            LibraryPath = $"/library/{title}",
            LinkerName = library,
            QBittorrentCategory = category,
            ChosenAudioExtension = format,
            UpgradeSearchedAt = searchedAt,
        };
    }

    private static void Add(Harness harness, params TorrentEntity[] copies)
    {
        harness.Context.Torrents.AddRange(copies);
        harness.Context.SaveChanges();
    }

    private TorrentEntity Stored(Harness harness, string id) => harness.Context.Torrents.AsNoTracking().Single(t => t.Id == id);

    [Fact]
    public async Task ABetterCopy_IsSelected_IntoTheOldCopysLibraryCategory()
    {
        var harness = CreateHarness(Results(Record(1, filetype: "mp3"), Record(2, filetype: "m4b")));
        Add(harness, Copy("hash1"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 1), (summary.Searched, summary.Selected));
        var selected = Assert.Single(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Equal((2L, "audiobooks", "Upgrade"), (selected.MamId, selected.Category, selected.GrabberName));
        Assert.Equal(Now, Stored(harness, "hash1").UpgradeSearchedAt);
        var search = Assert.Single(harness.Searches);
        Assert.Contains("\"text\":\"Dune Frank Herbert\"", search);
        Assert.Contains("\"srchIn\":[\"title\",\"author\"]", search);
        Assert.Contains("\"browse_lang\":[1]", search);
    }

    [Fact]
    public async Task NoBetterCopy_SameFormatWorseOrAnotherBook_SelectsNothing_ButCountsAsSearched()
    {
        var harness = CreateHarness(Results(
            Record(1, filetype: "mp3"),
            Record(2, filetype: "m4b", author: "Brian Herbert"),
            Record(3, title: "Dune Messiah", filetype: "m4b")));
        Add(harness, Copy("hash1"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 0), (summary.Searched, summary.Selected));
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Equal(Now, Stored(harness, "hash1").UpgradeSearchedAt);
    }

    [Fact]
    public async Task ABetterCopyThatIsNotFree_IsNotTaken_WithTheDefaultFreeBlock()
    {
        var harness = CreateHarness(Results(Record(2, filetype: "m4b", free: false)));
        Add(harness, Copy("hash1"));

        await harness.RunAsync();

        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
    }

    [Fact]
    public async Task OnlyCopiesThatCouldBeBetter_AreSearched_AndOnlyWhenDue()
    {
        var harness = CreateHarness(Results());
        Add(
            harness,
            Copy("best", format: "m4b"),
            Copy("recent", title: "Searched Last Week", searchedAt: Now.AddDays(-7)),
            Copy("old", title: "Searched Long Ago", searchedAt: Now.AddDays(-40)),
            Copy("never", title: "Never Searched"));

        await harness.RunAsync(Settings(searchesPerRun: 2));

        Assert.Equal(2, harness.Searches.Count);
        Assert.Contains("Never Searched", harness.Searches[0]);
        Assert.Contains("Searched Long Ago", harness.Searches[1]);
    }

    [Fact]
    public async Task Libraries_LimitTheCopiesSearched()
    {
        var harness = CreateHarness(Results());
        Add(harness, Copy("a", title: "In Audiobooks"), Copy("b", title: "In Other", library: "Other", category: "other"));

        await harness.RunAsync(Settings(libraries: "Other"));

        Assert.Contains("In Other", Assert.Single(harness.Searches));
    }

    [Fact]
    public async Task TheSelectionLimit_StopsTheRun()
    {
        var harness = CreateHarness(body => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.Contains("Messiah", StringComparison.Ordinal)
                ? Results(Record(4, title: "Dune Messiah", filetype: "m4b"))
                : Results(Record(2, filetype: "m4b"))),
        }, unsatLimit: 1);
        Add(harness, Copy("hash1"), Copy("hash2", title: "Dune Messiah"));

        var summary = await harness.RunAsync();

        Assert.Equal((1, 1), (summary.Searched, summary.Selected));
        Assert.Null(Stored(harness, "hash2").UpgradeSearchedAt);
    }

    [Fact]
    public async Task OneCopyFailing_IsRecordedOnTheErrorsPage_AndMamRefusingTheSessionFailsTheRun()
    {
        var harness = CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
        Add(harness, Copy("hash1"));

        var summary = await harness.RunAsync();

        Assert.Equal(1, summary.Failed);
        var error = Assert.Single(harness.Context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.Upgrade, "hash1", "Dune"), (error.Stage, error.Key, error.Title));
        Assert.Equal(Now, Stored(harness, "hash1").UpgradeSearchedAt);

        var refused = CreateHarness(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        refused.Context.Torrents.Single().UpgradeSearchedAt = null;
        refused.Context.SaveChanges();
        await Assert.ThrowsAsync<MamForbiddenException>(() => refused.RunAsync());
    }

    [Fact]
    public async Task ADryRun_WritesNothing()
    {
        var harness = CreateHarness(Results(Record(2, filetype: "m4b")));
        Add(harness, Copy("hash1"));

        var summary = await harness.RunAsync(Settings(dryRun: true));

        Assert.Equal((1, true), (summary.Selected, summary.DryRun));
        Assert.Empty(harness.Context.SelectedTorrents.AsNoTracking());
        Assert.Null(Stored(harness, "hash1").UpgradeSearchedAt);
    }
}

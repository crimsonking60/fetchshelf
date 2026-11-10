using System.Net;
using System.Text.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class AutograbberRunnerMarkRemovedTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-autograbber-mark-removed-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string AccountStateJson = """
        {
            "ratio": 5.0, "uploaded_bytes": 1000, "downloaded_bytes": 100, "wedges": 10, "uid": 1,
            "snatch_summary": { "unsat": { "count": 0, "limit": 20 } }
        }
        """;

    /// <summary>A fake MAM whose search returns <paramref name="searchResults"/>, and whose lookup by id finds only <paramref name="onMam"/>.</summary>
    private sealed class FakeMam(IReadOnlyList<long> searchResults, IReadOnlySet<long> onMam)
    {
        public List<long> LookedUp { get; } = [];

        public MamClient CreateClient()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("jsonLoad.php"))
                {
                    return Json(AccountStateJson);
                }

                if (!path.Contains("loadSearchJSONbasic.php"))
                {
                    return Json("{}");
                }

                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (body.RootElement.GetProperty("tor").TryGetProperty("id", out var id))
                {
                    LookedUp.Add(id.GetInt64());
                    return Json(Page(onMam.Contains(id.GetInt64()) ? [id.GetInt64()] : []));
                }

                var start = body.RootElement.GetProperty("tor").TryGetProperty("startNumber", out var s) ? s.GetInt64() : 0;
                return Json(Page(start == 0 ? searchResults : []));
            });

            return new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private static string Page(IReadOnlyList<long> ids) => ids.Count == 0
            ? """{"error":"Nothing returned, out of 0"}"""
            : $$"""{"perpage":100,"start":0,"total":{{ids.Count}},"found":{{ids.Count}},"data":[{{string.Join(",", ids.Select(Record))}}]}""";

        private static string Record(long id) => $$"""
            {
                "id": {{id}}, "title": "Result {{id}}", "author_info": "{\"1\":\"Author\"}",
                "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                "seeders": 10, "leechers": 1, "times_completed": 1,
                "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                "vip_expire": 0, "dl": "dl{{id}}"
            }
            """;
    }

    private static AutograbEntry Entry(bool markRemoved = true, bool dryRun = false) => new()
    {
        Name = "Freeleech",
        Type = AutograbType.Freeleech,
        Cost = ConfiguredCost.Ratio,
        MaxPages = 1,
        MarkRemoved = markRemoved,
        DryRun = dryRun,
        // Nothing passes, so the runs only search and check: selections are other tests' business.
        MinSeeders = 1000,
    };

    private static async Task SeedAsync(FetchshelfDbContext context, long mamId, DateTimeOffset? checkedAt = null, DateTimeOffset? removedAt = null)
    {
        var metadata = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: mamId, title: $"Book {mamId}", authorName: "Some Author"));
        context.Torrents.Add(new TorrentEntity
        {
            Id = $"hash{mamId}",
            MamId = mamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            MamCheckedAt = checkedAt,
            RemovedFromMamAt = removedAt,
        });
        await context.SaveChangesAsync();
    }

    private static AutograbberRunner Runner(FetchshelfDbContext context, MamClient client, ILogger<AutograbberRunner>? logger = null) =>
        new(client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context, logger);

    private static TorrentEntity Stored(FetchshelfDbContext context, long mamId)
    {
        context.ChangeTracker.Clear();
        return context.Torrents.Single(t => t.MamId == mamId);
    }

    [Fact]
    public async Task RunAsync_StoredTorrentInTheGapThatMamNoLongerHas_IsMarkedRemoved()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105);
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long>());
        var logger = new ListLogger<AutograbberRunner>();

        await Runner(context, mam.CreateClient(), logger).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Equal([105L], mam.LookedUp);
        var torrent = Stored(context, 105);
        Assert.NotNull(torrent.RemovedFromMamAt);
        Assert.NotNull(torrent.MamCheckedAt);
        var removed = Assert.Single(context.Events, e => e.Kind == EventKind.RemovedFromMam);
        Assert.Equal(("hash105", 105L), (removed.TorrentId, removed.MamId));
        Assert.Contains(
            logger.Messages(LogLevel.Information),
            m => m == "Autograb Freeleech: MAM no longer has Book 105 by Some Author (MAM id 105); marked as removed from MAM");
    }

    [Fact]
    public async Task RunAsync_StoredTorrentInTheGapThatMamStillHas_OnlyRecordsTheCheck()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105);
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long> { 105 });

        await Runner(context, mam.CreateClient()).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Equal([105L], mam.LookedUp);
        var torrent = Stored(context, 105);
        Assert.Null(torrent.RemovedFromMamAt);
        Assert.NotNull(torrent.MamCheckedAt);
        Assert.Empty(context.Events);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(8, true)]
    public async Task RunAsync_TorrentCheckedBefore_IsCheckedAgainOnlyAfterAWeek(int daysAgo, bool expectLookup)
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105, checkedAt: DateTimeOffset.UtcNow.AddDays(-daysAgo));
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long> { 105 });

        await Runner(context, mam.CreateClient()).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Equal(expectLookup, mam.LookedUp.Count == 1);
    }

    [Fact]
    public async Task RunAsync_MoreCandidatesThanTheLimit_ChecksTheLimitAskedAboutLongestAgoFirst()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var now = DateTimeOffset.UtcNow;
        for (var id = 101; id <= 125; id++)
        {
            // 101-105 were never checked; the rest were checked longer ago the higher their id.
            await SeedAsync(context, id, checkedAt: id <= 105 ? null : now.AddDays(-(id - 100)));
        }

        var mam = new FakeMam(searchResults: [100, 200], onMam: Enumerable.Range(101, 25).Select(i => (long)i).ToHashSet());

        await Runner(context, mam.CreateClient()).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Equal(AutograbberRunner.MaxRemovalChecksPerRun, mam.LookedUp.Count);
        long[] expected = [101, 102, 103, 104, 105, 125, 124, 123, 122, 121, 120, 119, 118, 117, 116, 115, 114, 113, 112, 111];
        Assert.Equal(expected, mam.LookedUp);
    }

    [Fact]
    public async Task RunAsync_TorrentOutsideTheRangeOrAlreadyMarkedOrReturned_IsNotLookedUp()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 99);
        await SeedAsync(context, 111);
        await SeedAsync(context, 105, removedAt: DateTimeOffset.UtcNow.AddDays(-30));
        await SeedAsync(context, 110);
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long>());

        await Runner(context, mam.CreateClient()).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Empty(mam.LookedUp);
    }

    [Fact]
    public async Task RunAsync_DryRun_LooksUpButWritesNothing()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105);
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long>());
        var logger = new ListLogger<AutograbberRunner>();

        await Runner(context, mam.CreateClient(), logger).RunAsync(Entry(dryRun: true), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Equal([105L], mam.LookedUp);
        var torrent = Stored(context, 105);
        Assert.Null(torrent.RemovedFromMamAt);
        Assert.Null(torrent.MamCheckedAt);
        Assert.Empty(context.Events);
        Assert.Contains(logger.Messages(LogLevel.Information), m => m.Contains("would have been marked as removed from MAM (dry run)"));
    }

    [Fact]
    public async Task RunAsync_WithoutMarkRemoved_LooksNothingUp()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105);
        var mam = new FakeMam(searchResults: [100, 110], onMam: new HashSet<long>());

        await Runner(context, mam.CreateClient()).RunAsync(Entry(markRemoved: false), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Empty(mam.LookedUp);
        Assert.Null(Stored(context, 105).RemovedFromMamAt);
    }

    [Fact]
    public async Task RunAsync_NoResults_LooksNothingUp()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await SeedAsync(context, 105);
        var mam = new FakeMam(searchResults: [], onMam: new HashSet<long>());

        await Runner(context, mam.CreateClient()).RunAsync(Entry(), globalUnsatBuffer: 0, ignoreTorrents: []);

        Assert.Empty(mam.LookedUp);
    }
}

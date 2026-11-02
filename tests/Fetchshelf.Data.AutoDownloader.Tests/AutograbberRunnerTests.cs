using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class AutograbberRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-autograbber-runner-tests-").FullName;

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

    private static string SearchResponseWithTwoTorrents() => """
        {
            "perpage": 100,
            "start": 0,
            "total": 2,
            "found": 2,
            "data": [
                {
                    "id": 1, "title": "High Seeders", "author_info": "{\"1\":\"Author A\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 10, "leechers": 1, "times_completed": 1,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dl1"
                },
                {
                    "id": 2, "title": "Low Seeders", "author_info": "{\"1\":\"Author B\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 2, "leechers": 1, "times_completed": 1,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dl2"
                }
            ]
        }
        """;

    private const string EmptyResultJson = """{"perpage":100,"start":0,"total":0,"found":0,"data":[]}""";

    private (MamClient Client, List<string> SearchRequestPaths) CreateFakeClient(string searchResponseJson)
    {
        var searchRequestPaths = new List<string>();
        var firstSearchServed = false;
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                searchRequestPaths.Add(path);
                var body = firstSearchServed ? EmptyResultJson : searchResponseJson;
                firstSearchServed = true;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        var client = new MamClient(
            new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
        return (client, searchRequestPaths);
    }

    private static AutograbEntry Entry(long? minSeeders = null) => new()
    {
        Type = AutograbType.Bookmarks,
        Cost = ConfiguredCost.Ratio,
        MinSeeders = minSeeders,
        MaxPages = 1,
    };

    [Fact]
    public async Task RunAsync_ServerMatchFailingLocalFilter_IsExcluded()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var (client, _) = CreateFakeClient(SearchResponseWithTwoTorrents());
        var runner = new AutograbberRunner(
            client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context);

        var selectedCount = await runner.RunAsync(Entry(minSeeders: 8), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(1, selectedCount);
        var selected = Assert.Single(context.SelectedTorrents);
        Assert.Equal(1, selected.MamId);
    }

    [Fact]
    public async Task RunAsync_ZeroBudget_PerformsNoSearch()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        // unsat count already at the limit minus buffer => budget 0.
        var (client, searchRequestPaths) = CreateFakeClient(SearchResponseWithTwoTorrents());
        var runner = new AutograbberRunner(
            client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context);

        var selectedCount = await runner.RunAsync(Entry(), globalUnsatBuffer: 20, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(0, selectedCount);
        Assert.Empty(searchRequestPaths);
    }

    [Fact]
    public async Task RunAsync_LimitReached_LogsTheLimitAndWhereItComesFrom()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var (client, _) = CreateFakeClient(SearchResponseWithTwoTorrents());
        var logger = new ListLogger<AutograbberRunner>();
        var runner = new AutograbberRunner(
            client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context,
            logger);

        // Unsat limit 20, none in use, buffer 19 => a limit of 1.
        var selectedCount = await runner.RunAsync(Entry(), globalUnsatBuffer: 19, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(1, selectedCount);
        Assert.Contains(
            logger.Messages(LogLevel.Information),
            m => m.EndsWith(
                "2 search results, 1 selected; stopped at this run's limit of 1 selections "
                + "(unsatisfied-snatch slots: 20 allowed - 0 in use - unsat_buffer 19)",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_DryRun_SelectsNothingReturnsZeroAndLogsWhatItWouldSelect()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var (client, _) = CreateFakeClient(SearchResponseWithTwoTorrents());
        var logger = new ListLogger<AutograbberRunner>();
        var runner = new AutograbberRunner(
            client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context,
            logger);
        var entry = Entry();
        entry.Name = "Bookmarks";
        entry.DryRun = true;

        var selectedCount = await runner.RunAsync(entry, globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(0, selectedCount);
        Assert.Empty(context.SelectedTorrents);
        var messages = logger.Messages(LogLevel.Information);
        Assert.Contains(messages, m => m.Contains("Autograb Bookmarks: dry run"));
        Assert.Contains(messages, m => m.Contains("MAM torrent 1 \"High Seeders\": would be selected"));
        Assert.Contains(messages, m => m.Contains("MAM torrent 2 \"Low Seeders\": would be selected"));
        Assert.Contains(messages, m => m.Contains("2 search results, 2 would have been selected (dry run)"));
    }

    [Fact]
    public async Task RunAsync_NotADryRun_LogsASummaryAndKeepsPerTorrentDetailsAtDebugLevel()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var (client, _) = CreateFakeClient(SearchResponseWithTwoTorrents());
        var logger = new ListLogger<AutograbberRunner>();
        var runner = new AutograbberRunner(
            client, new MamAccessGate(), new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists), context,
            logger);

        var selectedCount = await runner.RunAsync(Entry(minSeeders: 8), globalUnsatBuffer: 0, globalWedgeBuffer: 0, ignoreTorrents: []);

        Assert.Equal(1, selectedCount);
        Assert.Contains(logger.Messages(LogLevel.Information), m => m.Contains("2 search results, 1 selected"));
        Assert.Contains(logger.Messages(LogLevel.Debug), m => m.Contains("MAM torrent 1 \"High Seeders\": selected"));
        Assert.Contains(logger.Messages(LogLevel.Debug), m => m.Contains("MAM torrent 2 \"Low Seeders\" does not pass the filter"));
    }
}

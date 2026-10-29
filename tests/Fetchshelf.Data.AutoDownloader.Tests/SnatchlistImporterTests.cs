using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class SnatchlistImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-snatchlist-importer-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string AccountStateJson = """
        {"ratio":5.0,"uploaded_bytes":1000,"downloaded_bytes":100,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
        """;

    private static string SnatchlistPageJson(bool empty) => empty
        ? """{"rows":[]}"""
        : """{"rows":[{"id":1,"title":"The Way of Kings","seeders":5,"leechers":1}]}""";

    private MamClient CreateMamClient()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (path.Contains("loadUserDetailsTorrents.php"))
            {
                var iteration = request.RequestUri!.Query.Contains("iteration=0");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistPageJson(empty: !iteration)) };
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "dl1", title: "The Way of Kings", authorName: "A")),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        return new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
    }

    [Fact]
    public async Task RunAsync_ImportsRowAsMetadataOnlyEntry()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());

        var created = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat });

        Assert.Equal(1, created);
        var entry = Assert.Single(context.Torrents);
        Assert.Equal("mam:1", entry.Id);
    }

    [Fact]
    public async Task RunAsync_ImportedEntry_IsTreatedAsOwnedByLaterSelection()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());
        await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat });

        // A worse-ranked candidate for the same book should become a duplicate of the imported entry.
        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, title: "The Way of Kings", authorName: "A", fileTypes: ["mp3"]);
        var selectionRunner = new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists);

        var outcome = await selectionRunner.ProcessCandidateAsync(
            candidateRecord, MamTorrentMapper.Map(candidateRecord), [], ConfiguredCost.Ratio, null, null, 0, 0);

        Assert.Equal(SelectionOutcome.DuplicateOfLibrary, outcome);
    }

    [Fact]
    public async Task RunAsync_DryRun_CreatesNothingAndLogsWhatItWouldRecord()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate(), logger);

        var created = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, DryRun = true });

        Assert.Equal(0, created);
        Assert.Empty(context.Torrents);
        var messages = logger.Messages(LogLevel.Information);
        Assert.Contains(messages, m => m.Contains("MaM torrent 1 \"The Way of Kings\" would be recorded as owned"));
        Assert.Contains(messages, m => m.Contains("read 1 snatch list rows, 1 would have been recorded as owned (dry run)"));
    }
}

using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
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
    public async Task RunAsync_MetadataOnlyAdd_ImportsRowAsMetadataOnlyEntry()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());

        var created = (await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd })).Recorded;

        Assert.Equal(1, created);
        var entry = Assert.Single(context.Torrents);
        Assert.Equal("mam:1", entry.Id);
    }

    [Fact]
    public async Task RunAsync_MetadataOnly_SkipsTorrentThatIsNotStored()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate(), logger);

        var created = (await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat })).Recorded;

        Assert.Equal(0, created);
        Assert.Empty(context.Torrents);
        Assert.Contains(logger.Messages(LogLevel.Debug), m => m.Contains("The Way of Kings (MAM id 1) is not stored, skipped"));
    }

    [Fact]
    public async Task RunAsync_MetadataOnly_RefreshesStoredTorrent()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var oldMetadata = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 1, title: "Old Title"));
        context.Torrents.Add(new TorrentEntity
        {
            Id = "stored",
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(oldMetadata.Title),
            Title = oldMetadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(oldMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate(), logger);

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat });

        Assert.Equal(new SnatchlistRunSummary(Rows: 1, Recorded: 0, Updated: 1, Failed: 0), summary);
        var entry = Assert.Single(context.Torrents);
        Assert.Equal("stored", entry.Id);
        Assert.Equal("The Way of Kings", entry.Title);
        Assert.Contains(
            logger.Messages(LogLevel.Information),
            m => m.EndsWith("The Way of Kings by A had its metadata updated from MAM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_StoredTorrentAlreadyUpToDate_SaysThereIsNothingToDo()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate())
            .RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd });
        var logger = new ListLogger<SnatchlistImporter>();

        var summary = await new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate(), logger)
            .RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Name = "unsat" });

        Assert.Equal(new SnatchlistRunSummary(Rows: 1, Recorded: 0, Updated: 0, Failed: 0), summary);
        Assert.Equal(
            ["Snatchlist unsat: read 1 snatch list rows from 1 page, nothing to do: everything is up to date"],
            logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_EmptySnatchList_SaysSo()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var handler = new FakeHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath.Contains("jsonLoad.php") ? AccountStateJson : SnatchlistPageJson(empty: true)),
            });
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(
            context, new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }), new MamAccessGate(), logger);

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Name = "unsat" });

        Assert.Equal(new SnatchlistRunSummary(Rows: 0, Recorded: 0, Updated: 0, Failed: 0), summary);
        Assert.Equal(["Snatchlist unsat: the snatch list is empty, nothing to do"], logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_LookupFailsForOneRow_LogsErrorCountsItAndImportsTheRest()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        const string twoRows =
            """{"rows":[{"id":1,"title":"Broken","seeders":5,"leechers":1},{"id":2,"title":"Fine","seeders":5,"leechers":1}]}""";
        var searches = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (request.RequestUri.AbsolutePath.Contains("loadUserDetailsTorrents.php"))
            {
                var rows = request.RequestUri.Query.Contains("iteration=0") ? twoRows : SnatchlistPageJson(empty: true);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rows) };
            }

            // Rows are looked up in order: the first lookup (torrent 1) fails, the second (torrent 2) works.
            return ++searches == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(2, "dl2", title: "Fine", authorName: "A")),
                };
        });
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(
            context, new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }), new MamAccessGate(), logger);

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd });

        Assert.Equal(new SnatchlistRunSummary(Rows: 2, Recorded: 1, Updated: 0, Failed: 1), summary);
        Assert.Contains("could not import MAM torrent 1", Assert.Single(logger.Messages(LogLevel.Error)));
        Assert.Equal(2, Assert.Single(context.Torrents).MamId);
    }

    private const string BrokenAndFineRows =
        """{"rows":[{"id":1,"title":"Broken","seeders":5,"leechers":1},{"id":2,"title":"Fine","seeders":5,"leechers":1}]}""";

    /// <summary>A MAM with the rows Broken (1) and Fine (2), whose lookups answer as <paramref name="lookup"/> says for each MAM id.</summary>
    private static MamClient TwoRowMam(Func<long, HttpResponseMessage> lookup) =>
        new(new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (uri.AbsolutePath.Contains("loadUserDetailsTorrents.php"))
            {
                var rows = uri.Query.Contains("iteration=0") ? BrokenAndFineRows : SnatchlistPageJson(empty: true);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rows) };
            }

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return lookup(body.Contains("\"id\":1", StringComparison.Ordinal) || body.Contains("\"id\":\"1\"", StringComparison.Ordinal) ? 1 : 2);
        })), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);

    private static HttpResponseMessage Found(long mamId, string title) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(DownloadTestSupport.TorrentRecordJson(mamId, $"dl{mamId}", title: title, authorName: "A")),
    };

    private static readonly SnatchlistEntry MetadataOnlyAdd = new() { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd };

    [Fact]
    public async Task RunAsync_RowFailsThenImports_RecordsItsErrorAndClearsItOnTheNextRun()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var broken = true;
        var mam = TwoRowMam(id => id == 1 && broken ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Found(id, id == 1 ? "Broken" : "Fine"));

        await new SnatchlistImporter(context, mam, new MamAccessGate()).RunAsync(MetadataOnlyAdd);

        var error = Assert.Single(context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.Snatchlist, "1", "Broken"), (error.Stage, error.Key, error.Title));
        Assert.Contains("500", error.ErrorText);

        broken = false;
        await new SnatchlistImporter(context, mam, new MamAccessGate()).RunAsync(MetadataOnlyAdd);

        Assert.Empty(context.ErroredTorrents.AsNoTracking());
    }

    [Fact]
    public async Task RunAsync_MamRefusesTheSession_FailsTheRunKeepsWhatItImportedAndRecordsNoRowErrors()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        // Rows are imported in order: Fine (2) can't come first, so Broken (1) is refused and nothing after it is read.
        var mam = TwoRowMam(id => id == 1 ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Found(id, "Fine"));

        await Assert.ThrowsAsync<MamForbiddenException>(() => new SnatchlistImporter(context, mam, new MamAccessGate()).RunAsync(MetadataOnlyAdd));

        Assert.Empty(context.ErroredTorrents.AsNoTracking());
    }

    [Fact]
    public async Task RunAsync_DryRunWithAFailingRow_RecordsNothing()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var mam = TwoRowMam(id => id == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Found(id, "Fine"));

        var summary = await new SnatchlistImporter(context, mam, new MamAccessGate()).RunAsync(
            new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd, DryRun = true });

        Assert.Equal(1, summary.Failed);
        Assert.Empty(context.ErroredTorrents.AsNoTracking());
    }

    [Fact]
    public async Task RunAsync_ExcludeUploader_IsCheckedAgainstTheTorrentsUploader()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());

        var created = (await importer.RunAsync(new SnatchlistEntry
        {
            Type = SnatchlistType.Unsat,
            Cost = ConfiguredCost.MetadataOnlyAdd,
            ExcludeUploader = ["uploader"],
        })).Recorded;

        Assert.Equal(0, created);
        Assert.Empty(context.Torrents);
    }

    [Theory]
    [InlineData(1L, null, 1)]
    [InlineData(2L, null, 0)]
    [InlineData(null, 0L, 0)]
    public async Task RunAsync_SnatchedBounds_AreCheckedAgainstTheTorrentsCompletionCount(long? minSnatched, long? maxSnatched, int expectedCreated)
    {
        // The search record reports times_completed = 1.
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());

        var created = (await importer.RunAsync(new SnatchlistEntry
        {
            Type = SnatchlistType.Unsat,
            Cost = ConfiguredCost.MetadataOnlyAdd,
            MinSnatched = minSnatched,
            MaxSnatched = maxSnatched,
        })).Recorded;

        Assert.Equal(expectedCreated, created);
    }

    [Fact]
    public async Task RunAsync_ImportedEntry_IsTreatedAsOwnedByLaterSelection()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate());
        await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd });

        // A worse-ranked candidate for the same book should become a duplicate of the imported entry.
        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, title: "The Way of Kings", authorName: "A", fileTypes: ["mp3"]);
        var selectionRunner = new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists);

        var outcome = await selectionRunner.ProcessCandidateAsync(
            candidateRecord, MamTorrentMapper.Map(candidateRecord), [], ConfiguredCost.Ratio, null, null);

        Assert.Equal(SelectionOutcome.DuplicateOfLibrary, outcome);
    }

    [Fact]
    public async Task RunAsync_DryRun_CreatesNothingAndLogsWhatItWouldRecord()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(context, CreateMamClient(), new MamAccessGate(), logger);

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.Unsat, Cost = ConfiguredCost.MetadataOnlyAdd, DryRun = true });

        // The summary counts what would have been recorded; nothing is written.
        Assert.Equal((1, true), (summary.Recorded, summary.DryRun));
        Assert.Empty(context.Torrents);
        var messages = logger.Messages(LogLevel.Information);
        Assert.Contains(messages, m => m.Contains("The Way of Kings by A would be recorded as owned"));
        Assert.Contains(messages, m => m.Contains("read 1 snatch list rows from 1 page, 1 would have been recorded as owned (dry run)"));
    }

    private const string DetailedRowPageJson = """
        {"rows":[{
            "id":1,"title":"The Way of Kings","seeders":5,"leechers":1,
            "category":39,"categories":[],"author":[{"id":1,"name":"Brandon Sanderson"}],"narrator":[],"series":[],
            "fileTypes":[{"id":1,"name":"m4b"}],"size":"100 MiB","tags":"","browseFlags":0,
            "free":0,"vip":1,"vip_expire":null,"personalFree":0,"uploaderName":"uploader","uploaderID":1,"times_completed":10
        }]}
        """;

    /// <summary>Serves <see cref="DetailedRowPageJson"/> and counts the searches, which should not be needed.</summary>
    private static MamClient CreateDetailedRowClient(Action onSearch) =>
        new(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("jsonLoad.php"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
                }

                if (path.Contains("loadUserDetailsTorrents.php"))
                {
                    var json = request.RequestUri.Query.Contains("iteration=0") ? DetailedRowPageJson : SnatchlistPageJson(empty: true);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                }

                onSearch();
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            })),
            new MamClientOptions { MamId = "cookie" });

    private static async Task StoreAsync(FetchshelfDbContext context, TorrentMetadata metadata)
    {
        context.Torrents.Add(new TorrentEntity
        {
            Id = "stored",
            MamId = metadata.MamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task RunAsync_StoredTorrentWithDetailedRow_RefreshesFromTheRowWithoutSearching()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await StoreAsync(context, MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 1, title: "Old Title")));
        var searches = 0;
        var importer = new SnatchlistImporter(context, CreateDetailedRowClient(() => searches++), new MamAccessGate());

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.SeedSat, Cost = ConfiguredCost.MetadataOnlyAdd });

        Assert.Equal(0, searches);
        Assert.Equal(new SnatchlistRunSummary(Rows: 1, Recorded: 0, Updated: 1, Failed: 0), summary);
        var stored = TorrentMetadataSerializer.Deserialize(Assert.Single(context.Torrents).MetadataJson);
        Assert.Equal("The Way of Kings", stored.Title);
        Assert.Equal(VipStatusKind.Permanent, stored.VipStatus.Kind);
        Assert.Equal(Language.English, stored.Language);
    }

    [Fact]
    public async Task RunAsync_MetadataEditedByHand_KeepsTheEditsAndUpdatesOnlyTheVipStatus()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var edited = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 1)) with
        {
            Title = "My Own Title",
            MetadataSource = MetadataSource.Manual,
        };
        await StoreAsync(context, edited);
        var logger = new ListLogger<SnatchlistImporter>();
        var importer = new SnatchlistImporter(context, CreateDetailedRowClient(() => { }), new MamAccessGate(), logger);

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.SeedSat });

        Assert.Equal(1, summary.Updated);
        var stored = TorrentMetadataSerializer.Deserialize(Assert.Single(context.Torrents).MetadataJson);
        Assert.Equal("My Own Title", stored.Title);
        Assert.Equal(MetadataSource.Manual, stored.MetadataSource);
        Assert.Equal(VipStatusKind.Permanent, stored.VipStatus.Kind);
        Assert.Contains(logger.Messages(LogLevel.Information), m => m.Contains("its metadata was edited by hand and is kept"));
    }

    [Fact]
    public async Task RunAsync_NotStoredWithMetadataOnly_SkipsWithoutSearching()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var searches = 0;
        var importer = new SnatchlistImporter(context, CreateDetailedRowClient(() => searches++), new MamAccessGate());

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.SeedSat });

        Assert.Equal(0, searches);
        Assert.Equal(new SnatchlistRunSummary(Rows: 1, Recorded: 0, Updated: 0, Failed: 0), summary);
    }

    [Fact]
    public async Task RunAsync_MamSendsTheSameRowsForEveryPage_ReadsThemOnceAndStops()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var pageRequests = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            if (path.Contains("loadUserDetailsTorrents.php"))
            {
                pageRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistPageJson(empty: false)) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "dl1", title: "The Way of Kings", authorName: "A")),
            };
        });
        var importer = new SnatchlistImporter(
            context, new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }), new MamAccessGate());

        var summary = await importer.RunAsync(new SnatchlistEntry { Type = SnatchlistType.SeedSat, Cost = ConfiguredCost.MetadataOnlyAdd });

        Assert.Equal(2, pageRequests);
        Assert.Equal(new SnatchlistRunSummary(Rows: 1, Recorded: 1, Updated: 0, Failed: 0), summary);
    }
}

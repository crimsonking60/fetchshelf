using System.Net;
using System.Text;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class DownloadRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-download-runner-tests-").FullName;

    private static readonly byte[] TorrentBytes1 =
        Encoding.ASCII.GetBytes("d8:announce10:tracker://4:infod6:lengthi10e4:name4:testee");
    private const string TorrentHash1 = "e17c544bb145b9ec529225aeb904f53b22eedbf0";

    private static readonly byte[] TorrentBytes2 =
        Encoding.ASCII.GetBytes("d8:announce10:tracker://4:infod6:lengthi20e4:name5:test2ee");
    private const string TorrentHash2 = "3186d688772f06feafadfa22585aa2e84aa462a7";

    private string DbPath => Path.Combine(_root, "test.db");

    private static readonly SafetyBuffers NoBuffers = new(0, 0, []);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SelectedTorrentEntity BuildSelected(
        long mamId, SelectedTorrentCost cost, string downloadLink = "dl1") => new()
        {
            MamId = mamId,
            DownloadLink = downloadLink,
            CostDecision = cost.ToString(),
            MetadataJson = TorrentMetadataSerializer.Serialize(Metadata(mamId)),
            NormalizedTitleKey = TitleNormalizer.Normalize("T"),
            GrabberName = "grabber",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static TorrentMetadata Metadata(long mamId, long sizeBytes = 100) => new()
    {
        MamId = mamId,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language = Language.English,
        Title = "T",
        Authors = ["Some Author"],
        TotalSizeBytes = sizeBytes,
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private static HttpResponseMessage DownloadResponse(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task RunAsync_ApprovedRatioTorrent_StartsAndWritesGrabbedEvent()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(1, started);
        var torrent = Assert.Single(context.Torrents);
        Assert.Equal(TorrentHash1, torrent.Id);
        var @event = Assert.Single(context.Events);
        Assert.Equal(EventKind.Grabbed, @event.Kind);
        Assert.False(@event.WedgeUsed);
        Assert.NotNull(context.SelectedTorrents.Single().StartedAt);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeATorrentFileIsInHand_StopsWithTheTorrentStillQueued()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                cancellation.Cancel(); // the Cancel button, pressed before any torrent file is fetched
            }

            return request.RequestUri.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null;
        });
        var added = new List<byte[]>();
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], added)],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        Assert.Empty(added);
        context.ChangeTracker.Clear();
        Assert.Null(context.SelectedTorrents.Single().StartedAt);
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileAWedgeIsSpent_AddsAndRecordsThatTorrentThenStops()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.UseWedge, "dl1"));
        await Task.Delay(1);
        context.SelectedTorrents.Add(BuildSelected(2, SelectedTorrentCost.UseWedge, "dl2"));
        await context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var downloads = new List<string>();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.Contains("download.php"))
            {
                return null;
            }

            var withWedge = request.RequestUri.Query.Contains("&fl");
            downloads.Add(path.Split("download.php/")[1] + (withWedge ? " with wedge" : ""));
            if (withWedge)
            {
                cancellation.Cancel(); // the Cancel button, pressed while the wedge is being spent
            }

            return DownloadResponse(path.EndsWith("dl1", StringComparison.Ordinal) ? TorrentBytes1 : TorrentBytes2);
        });
        var added = new List<byte[]>();
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], added)],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        Assert.Equal(["dl1", "dl1 with wedge"], downloads);
        Assert.Single(added);
        context.ChangeTracker.Clear();
        Assert.Equal(TorrentHash1, Assert.Single(context.Torrents).Id);
        Assert.NotNull(context.SelectedTorrents.Single(s => s.MamId == 1).StartedAt);
        Assert.Null(context.SelectedTorrents.Single(s => s.MamId == 2).StartedAt);
        Assert.True(Assert.Single(context.Events).WedgeUsed);
    }

    [Fact(Timeout = 10_000)]
    public async Task RunAsync_AddingHangsAfterTheRunIsCancelled_GivesUpAfterTheGraceAndSaysAWedgeMayBeSpent()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.UseWedge));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var adding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var qb = new QBittorrentClient(
            new HttpClient(new HangingAddHandler(adding)), new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));
        var logger = new ListLogger<DownloadRunner>();
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false,
            safetyBuffers: NoBuffers, logger, finishAfterCancel: TimeSpan.FromMilliseconds(100));
        using var cancellation = new CancellationTokenSource();

        var run = runner.RunAsync(cancellation.Token);
        await adding.Task; // qBittorrent takes the torrent and never answers
        await cancellation.CancelAsync(); // Fetchshelf stopping

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(
            logger.Messages(LogLevel.Warning),
            m => m.Contains("was cut short", StringComparison.Ordinal) && m.Contains("A wedge may have been spent", StringComparison.Ordinal));
        context.ChangeTracker.Clear();
        Assert.Null(context.SelectedTorrents.Single().StartedAt);
    }

    /// <summary>A qBittorrent that logs in and lists nothing, and whose add never answers until cancelled.</summary>
    private sealed class HangingAddHandler(TaskCompletionSource adding) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("auth/login"))
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
                return login;
            }

            if (path.Contains("torrents/add"))
            {
                adding.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }
    }

    [Fact]
    public async Task RunAsync_GateHeldByAnotherJob_WaitsBeforeAskingMam()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        var mamCalls = 0;
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            mamCalls++;
            return request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null;
        });
        var gate = new MamAccessGate();
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, gate, [DownloadTestSupport.CreateQBittorrentClient([], [])], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);
        var otherJob = await gate.AcquireAsync("another job");

        var runTask = runner.RunAsync();

        Assert.False(runTask.IsCompleted);
        Assert.Equal(0, mamCalls);

        otherJob.Dispose();
        Assert.Equal(1, (await runTask).Started);
        Assert.NotEqual(0, mamCalls);
    }

    [Fact]
    public async Task RunAsync_NothingQueued_LogsSoWithoutAskingMam()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var mamCalls = 0;
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, _ =>
        {
            mamCalls++;
            return null;
        });
        var logger = new ListLogger<DownloadRunner>();
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers, logger);

        var summary = await runner.RunAsync();

        Assert.Equal(new DownloadRunSummary(Started: 0, Failed: 0, Queued: 0), summary);
        Assert.Equal(0, mamCalls);
        Assert.Equal(["No selected torrents are waiting to be downloaded"], logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_ApprovedRatioTorrent_LogsTitleAndAuthor()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var logger = new ListLogger<DownloadRunner>();
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], [])], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers, logger);

        await runner.RunAsync();

        Assert.Equal(
            [
                "Ratio budget for this run: 488.2 KiB (uploaded 976.6 KiB, downloaded 100 B, 0 B in flight, min_ratio 2)",
                "Added T by Some Author to qBittorrent (cost Ratio, category none)",
            ],
            logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_QBittorrentRefusesOneTorrent_LogsErrorKeepsItQueuedAndStartsTheRest()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var first = BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "dl1");
        var second = BuildSelected(2, SelectedTorrentCost.Ratio, downloadLink: "dl2");
        second.CreatedAt = first.CreatedAt.AddSeconds(1);
        context.SelectedTorrents.AddRange(first, second);
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php")
                ? DownloadResponse(request.RequestUri.AbsolutePath.Contains("dl1") ? TorrentBytes1 : TorrentBytes2)
                : null);
        var adds = 0;
        var qb = new QBittorrentClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("torrents/info"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
                }

                if (path.Contains("torrents/add"))
                {
                    return ++adds == 1
                        ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            })),
            new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));
        var logger = new ListLogger<DownloadRunner>();
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers, logger);

        var summary = await runner.RunAsync();

        Assert.Equal((1, 1), (summary.Started, summary.Failed));
        Assert.StartsWith("Could not download T by Some Author: ", Assert.Single(logger.Messages(LogLevel.Error)), StringComparison.Ordinal);
        var selections = await context.SelectedTorrents.AsNoTracking().OrderBy(s => s.MamId).ToListAsync();
        Assert.Null(selections[0].StartedAt);
        Assert.NotNull(selections[1].StartedAt);
        Assert.Equal(TorrentHash2, Assert.Single(context.Torrents).Id);
    }

    [Fact]
    public async Task RunAsync_StaleLinkRefreshedOnce_Succeeds()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();

        var attempt = 0;
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                attempt++;
                return attempt == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : DownloadResponse(TorrentBytes1);
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "fresh-link")),
                };
            }

            return null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(1, started);
        Assert.Single(context.Torrents);
    }

    [Fact]
    public async Task RunAsync_StillStaleAfterRefresh_RemovesSelectionAndRecordsError()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "fresh-link")),
                };
            }

            return null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var summary = await runner.RunAsync();

        Assert.Equal((0, 1), (summary.Started, summary.Failed));
        Assert.Empty(context.Torrents);
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
        var error = Assert.Single(context.ErroredTorrents);
        Assert.Equal(ErrorStage.Grabber, error.Stage);
        Assert.Equal(TorrentMetadataSerializer.Deserialize(context.SelectedTorrents.Single().MetadataJson).Title, error.Title);
    }

    private DownloadRunner RunnerFor(FetchshelfDbContext context, MamClient mam, QBittorrentClient? qb = null) =>
        new(context, new EventStore(context), mam, new MamAccessGate(), [qb ?? DownloadTestSupport.CreateQBittorrentClient([], [])],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

    private static HttpResponseMessage SearchResponse(long mamId, string downloadLink) =>
        new(HttpStatusCode.OK) { Content = new StringContent(DownloadTestSupport.TorrentRecordJson(mamId, downloadLink)) };

    private static HttpResponseMessage NoSearchResults() =>
        new(HttpStatusCode.OK) { Content = new StringContent("""{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""") };

    private static HttpResponseMessage NotATorrentFile() =>
        new(HttpStatusCode.OK) { Content = new StringContent("<html>Torrent not found</html>") };

    [Fact]
    public async Task RunAsync_ConnectionFailsOnEveryAttempt_KeepsTheSelectionQueuedAndGoesOn()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var first = BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "dl1");
        var second = BuildSelected(2, SelectedTorrentCost.Ratio, downloadLink: "dl2");
        second.CreatedAt = first.CreatedAt.AddSeconds(1);
        context.SelectedTorrents.AddRange(first, second);
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php/dl1"))
            {
                throw new HttpRequestException("Connection reset by peer");
            }

            return path.Contains("download.php/dl2") ? DownloadResponse(TorrentBytes2) : null;
        });

        var summary = await RunnerFor(context, mam).RunAsync();

        Assert.Equal((1, 1), (summary.Started, summary.Failed));
        var queued = context.SelectedTorrents.Single(s => s.MamId == 1);
        Assert.Null(queued.RemovedAt);
        Assert.Null(queued.StartedAt);
        Assert.Equal("dl1", queued.DownloadLink);
        Assert.Empty(context.ErroredTorrents);
        Assert.NotNull(context.SelectedTorrents.Single(s => s.MamId == 2).StartedAt);
    }

    [Fact]
    public async Task RunAsync_ServerErrorFetchingTheFile_KeepsTheSelectionQueued()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null);

        await RunnerFor(context, mam).RunAsync();

        Assert.Null(context.SelectedTorrents.Single().RemovedAt);
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task RunAsync_AnswerIsNotATorrentFile_RefreshesTheLinkAndStarts()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php/stale-link"))
            {
                return NotATorrentFile();
            }

            if (path.Contains("download.php/fresh-link"))
            {
                return DownloadResponse(TorrentBytes1);
            }

            return path.Contains("loadSearchJSONbasic.php") ? SearchResponse(1, "fresh-link") : null;
        });

        var summary = await RunnerFor(context, mam).RunAsync();

        Assert.Equal(1, summary.Started);
        Assert.Equal("fresh-link", context.SelectedTorrents.Single().DownloadLink);
    }

    [Fact]
    public async Task RunAsync_RefreshedLinkIsNotATorrentFileEither_RemovesTheSelectionRestorably()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                return NotATorrentFile();
            }

            return path.Contains("loadSearchJSONbasic.php") ? SearchResponse(1, "fresh-link") : null;
        });

        var summary = await RunnerFor(context, mam).RunAsync();

        Assert.Equal((0, 1), (summary.Started, summary.Failed));
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
        var error = Assert.Single(context.ErroredTorrents);
        Assert.Equal("Download link is stale and could not be refreshed.", error.ErrorText);
        Assert.NotNull(error.DisplayMetadataJson);
    }

    [Fact]
    public async Task RunAsync_LookupReturnsTheSameLink_RemovesTheSelectionAsStale()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();
        var downloads = 0;
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                downloads++;
                return NotATorrentFile();
            }

            return path.Contains("loadSearchJSONbasic.php") ? SearchResponse(1, "stale-link") : null;
        });

        await RunnerFor(context, mam).RunAsync();

        Assert.Equal(1, downloads);
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
        Assert.Equal("Download link is stale and could not be refreshed.", Assert.Single(context.ErroredTorrents).ErrorText);
    }

    [Fact]
    public async Task RunAsync_MamNoLongerHasTheTorrent_RemovesTheSelectionSayingSoWithoutRetry()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return path.Contains("loadSearchJSONbasic.php") ? NoSearchResults() : null;
        });

        var summary = await RunnerFor(context, mam).RunAsync();

        Assert.Equal((0, 1), (summary.Started, summary.Failed));
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
        var error = Assert.Single(context.ErroredTorrents);
        Assert.StartsWith("MAM no longer has this torrent", error.ErrorText);
        Assert.Null(error.DisplayMetadataJson);
    }

    [Fact]
    public async Task RunAsync_ConnectionFailsAfterTheLinkWasRefreshed_StaysQueuedWithTheNewLink()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "stale-link"));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php/stale-link"))
            {
                return NotATorrentFile();
            }

            if (path.Contains("download.php/fresh-link"))
            {
                throw new HttpRequestException("Connection reset by peer");
            }

            return path.Contains("loadSearchJSONbasic.php") ? SearchResponse(1, "fresh-link") : null;
        });

        await RunnerFor(context, mam).RunAsync();

        var selected = context.SelectedTorrents.Single();
        Assert.Null(selected.RemovedAt);
        Assert.Equal("fresh-link", selected.DownloadLink);
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task RunAsync_ForbiddenFetchingTheFile_StopsTheRunAndKeepsEverySelection()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var first = BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "dl1");
        var second = BuildSelected(2, SelectedTorrentCost.Ratio, downloadLink: "dl2");
        second.CreatedAt = first.CreatedAt.AddSeconds(1);
        context.SelectedTorrents.AddRange(first, second);
        await context.SaveChangesAsync();
        var downloads = 0;
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains("download.php"))
            {
                return null;
            }

            downloads++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var summary = await RunnerFor(context, mam).RunAsync();

        Assert.Equal((0, 1), (summary.Started, summary.Failed));
        Assert.Equal(1, downloads);
        Assert.All(context.SelectedTorrents, s => Assert.Null(s.RemovedAt));
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task RunAsync_AlreadyFinishedInClient_CreatesRecordAndDropsSelection()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var existingTorrent = new QBittorrentTorrent { Hash = TorrentHash1, Name = "T", SavePath = "/x", State = "uploading", Progress = 1.0 };
        var qb = DownloadTestSupport.CreateQBittorrentClient([existingTorrent], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        Assert.Equal("uploading", Assert.Single(context.Torrents).ClientStatus);
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
        Assert.Empty(context.Events);
    }

    [Fact]
    public async Task RunAsync_AlreadyDownloadingInClient_RecordsItAndMarksStarted()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var existingTorrent = new QBittorrentTorrent { Hash = TorrentHash1, Name = "T", SavePath = "/x", State = "downloading", Progress = 0.5 };
        var qb = DownloadTestSupport.CreateQBittorrentClient([existingTorrent], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        var torrent = Assert.Single(context.Torrents);
        Assert.Equal(TorrentHash1, torrent.Id);
        Assert.Equal("downloading", torrent.ClientStatus);
        Assert.NotNull(context.SelectedTorrents.Single().StartedAt);
        Assert.Null(context.SelectedTorrents.Single().RemovedAt);
    }

    [Fact]
    public async Task RunAsync_AlreadyLinkedInLibrary_DropsSelection()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        context.Torrents.Add(new TorrentEntity
        {
            Id = TorrentHash1,
            MamId = 1,
            NormalizedTitleKey = "t",
            Title = "T",
            MetadataJson = TorrentMetadataSerializer.Serialize(Metadata(1)),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        Assert.NotNull(context.SelectedTorrents.Single().RemovedAt);
    }

    [Fact]
    public async Task RunAsync_OverRatioBudget_SkipsRatioCostTorrent()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        // uploaded - downloaded is tiny, so the ratio budget in bytes is far below the torrent's size.
        const string tightAccountJson = """
            {"ratio":2.0,"uploaded_bytes":10,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(tightAccountJson, _ => null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        Assert.Null(context.SelectedTorrents.Single().StartedAt);
    }

    [Fact]
    public async Task RunAsync_RatioCostTorrentThatWouldTakeTheRatioBelowMinRatio_StaysQueued()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        // Ratio 1000/450 = 2.2. The 100-byte torrent would take it to 1000/550 = 1.8, below min_ratio 2: the budget
        // is 1000/2 - 450 = 50 bytes.
        const string accountJson = """
            {"ratio":2.2,"uploaded_bytes":1000,"downloaded_bytes":450,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        Assert.Null(context.SelectedTorrents.Single().StartedAt);
    }

    [Fact]
    public async Task RunAsync_UnsatBufferExhausted_SkipsTorrent()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();

        // unsat count 15, limit 20 -> only 5 free slots, less than the buffer of 10.
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":15,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(10, 0, []));

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
    }

    [Fact]
    public async Task RunAsync_OverRatioBudget_ReportsWhatItLeftQueuedAndWhichBudgetDidIt()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await Task.Delay(1);
        context.SelectedTorrents.Add(BuildSelected(2, SelectedTorrentCost.Ratio, "dl2"));
        await context.SaveChangesAsync();
        const string accountJson = """
            {"ratio":2.01,"uploaded_bytes":10,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], [])],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var summary = await runner.RunAsync();

        Assert.Equal((0, 2, 2), (summary.Started, summary.Queued, summary.LeftQueued));
        // As it stood when the first was held back: its own size against what was left.
        Assert.Equal("the ratio budget left is 5 B, less than the 100 B the first of them needs (min_ratio 2)", summary.LimitedBy);
    }

    [Fact]
    public async Task RunAsync_NoFreeUnsatSlot_ReportsWhatItLeftQueuedAndTheSlotsInUse()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":15,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], [])],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(10, 0, []));

        var summary = await runner.RunAsync();

        Assert.Equal((1, "no unsatisfied-snatch slot is free (15 of 20 in use, unsat_buffer 10)"), (summary.LeftQueued, summary.LimitedBy));
    }

    [Fact]
    public async Task RunAsync_EverythingStarted_LeavesNothingQueued()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        await context.SaveChangesAsync();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var runner = new DownloadRunner(
            context, new EventStore(context), mam, new MamAccessGate(), [DownloadTestSupport.CreateQBittorrentClient([], [])],
            minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var summary = await runner.RunAsync();

        Assert.Equal((1, 0, (string?)null), (summary.Started, summary.LeftQueued, summary.LimitedBy));
    }

    [Fact]
    public async Task RunAsync_ItsAutograbKeepsMoreSlotsFree_HoldsTheTorrentBack()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio)); // queued by "grabber"
        await context.SaveChangesAsync();

        // 5 free slots: enough for the global buffer of 0, not for the autograb's own 10.
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":15,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var buffers = new SafetyBuffers(0, 0, [new AutograbBuffers("grabber", UnsatBuffer: 10, WedgeBuffer: null)]);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: buffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
    }

    [Fact]
    public async Task RunAsync_UseWedgeAtBuffer_Fails()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.UseWedge));
        await context.SaveChangesAsync();

        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":5,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, _ => null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(0, 5, []));

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
    }

    [Fact]
    public async Task RunAsync_TryWedgeAlreadyFree_SkipsWedgeAndStarts()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.TryWedge));
        await context.SaveChangesAsync();

        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":5,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                return DownloadResponse(TorrentBytes1);
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "dl1", isFree: true)),
                };
            }

            return null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(0, 5, []));

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(1, started);
        var @event = Assert.Single(context.Events);
        Assert.False(@event.WedgeUsed);
    }

    [Fact]
    public async Task RunAsync_GlobalFreeleechNoLongerFree_SkipsDownload()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.GlobalFreeleech));
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                return DownloadResponse(TorrentBytes1);
            }

            if (path.Contains("loadSearchJSONbasic.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "dl1", isFree: false)),
                };
            }

            return null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(0, started);
        Assert.Empty(context.Torrents);
        Assert.Null(context.SelectedTorrents.Single().StartedAt);
    }

    [Fact]
    public async Task RunAsync_AccountBlocked406OnThirdOfFive_AbortsRemainingSelections()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        for (var i = 1; i <= 5; i++)
        {
            context.SelectedTorrents.Add(BuildSelected(i, SelectedTorrentCost.Ratio, downloadLink: $"dl{i}"));
            await Task.Delay(1); // ensure distinct CreatedAt ordering
        }

        await context.SaveChangesAsync();

        var attempted = new List<long>();
        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                var tid = long.Parse(request.RequestUri!.Query.Split("tid=")[1].Split('&')[0]);
                attempted.Add(tid);
                return attempted.Count switch
                {
                    3 => new HttpResponseMessage((HttpStatusCode)406),
                    1 => DownloadResponse(TorrentBytes1),
                    _ => DownloadResponse(TorrentBytes2),
                };
            }

            return null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(2, started);
        Assert.Equal([1, 2, 3], attempted);
    }

    [Fact]
    public async Task RunAsync_TryWedgeTorrentsInOneRun_StopUsingWedgesAtTheBuffer()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.TryWedge, downloadLink: "dl1"));
        await Task.Delay(1);
        context.SelectedTorrents.Add(BuildSelected(2, SelectedTorrentCost.TryWedge, downloadLink: "dl2"));
        await context.SaveChangesAsync();

        // 6 wedges and a buffer of 5: only one wedge may be used.
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":6,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var wedgedDownloads = new List<string>();
        var mam = DownloadTestSupport.CreateMamClient(accountJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("download.php"))
            {
                var dlHash = path.Split("download.php/")[1];
                if (request.RequestUri.Query.Contains("&fl"))
                {
                    wedgedDownloads.Add(dlHash);
                }

                return DownloadResponse(dlHash == "dl1" ? TorrentBytes1 : TorrentBytes2);
            }

            return path.Contains("loadSearchJSONbasic.php")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DownloadTestSupport.TorrentRecordJson(1, "dl1")) }
                : null;
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(0, 5, []));

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(2, started);
        Assert.Equal(["dl1"], wedgedDownloads);
    }

    [Fact]
    public async Task RunAsync_TorrentAlreadyInClient_SpendsNoWedge_SoTheNextOneGetsIt()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.UseWedge, downloadLink: "dl1"));
        await Task.Delay(1);
        context.SelectedTorrents.Add(BuildSelected(2, SelectedTorrentCost.UseWedge, downloadLink: "dl2"));
        await context.SaveChangesAsync();

        // 6 wedges and a buffer of 5: only one wedge may be used.
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":6,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
            """;
        var downloads = new List<string>();
        var mam = DownloadTestSupport.CreateMamClient(accountJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.Contains("download.php"))
            {
                return null;
            }

            var dlHash = path.Split("download.php/")[1];
            downloads.Add(request.RequestUri.Query.Contains("&fl") ? $"{dlHash} with wedge" : dlHash);
            return DownloadResponse(dlHash == "dl1" ? TorrentBytes1 : TorrentBytes2);
        });

        // The first torrent is in qBittorrent already, in a category Fetchshelf knows nothing about.
        var existingTorrent = new QBittorrentTorrent { Hash = TorrentHash1, Name = "T", SavePath = "/x", State = "downloading", Progress = 0.5, Category = "other" };
        var qb = DownloadTestSupport.CreateQBittorrentClient([existingTorrent], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(0, 5, []));

        await runner.RunAsync();

        Assert.Equal(["dl1", "dl2", "dl2 with wedge"], downloads);
        Assert.NotNull(context.SelectedTorrents.Single(s => s.MamId == 2).StartedAt);
    }

    [Fact]
    public async Task RunAsync_SecondTorrentInSameRun_SeesFirstsUnsatConsumption_WithoutSecondAccountFetch()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio, downloadLink: "dl1"));
        await Task.Delay(1);
        context.SelectedTorrents.Add(BuildSelected(2, SelectedTorrentCost.Ratio, downloadLink: "dl2"));
        await context.SaveChangesAsync();

        // 2 free slots with a buffer of 1: the first torrent fits (2 > 1), and starting it leaves 1, which no longer does.
        const string accountJson = """
            {"ratio":5.0,"uploaded_bytes":1000000,"downloaded_bytes":0,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":18,"limit":20}}}
            """;
        var accountStateFetchCount = 0;
        var mam = DownloadTestSupport.CreateMamClient(accountJson, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                accountStateFetchCount++;
            }

            if (!path.Contains("download.php"))
            {
                return null;
            }

            var dlHash = path.Split("download.php/")[1].Split('?')[0];
            return DownloadResponse(dlHash == "dl1" ? TorrentBytes1 : TorrentBytes2);
        });
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: new SafetyBuffers(1, 0, []));

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(1, started);
        Assert.Equal(1, accountStateFetchCount);
    }

    [Fact]
    public async Task RunAsync_SuccessfulRetry_ClearsPriorGrabberError()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.Add(BuildSelected(1, SelectedTorrentCost.Ratio));
        context.ErroredTorrents.Add(new ErroredTorrentEntity
        {
            Stage = ErrorStage.Grabber,
            Key = "1",
            ErrorText = "previous failure",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var mam = DownloadTestSupport.CreateMamClient(DownloadTestSupport.DefaultAccountStateJson, request =>
            request.RequestUri!.AbsolutePath.Contains("download.php") ? DownloadResponse(TorrentBytes1) : null);
        var qb = DownloadTestSupport.CreateQBittorrentClient([], []);
        var runner = new DownloadRunner(context, new EventStore(context), mam, new MamAccessGate(), [qb], minRatio: 2, addTorrentsStopped: false, safetyBuffers: NoBuffers);

        var started = (await runner.RunAsync()).Started;

        Assert.Equal(1, started);
        Assert.Empty(context.ErroredTorrents);
    }
}

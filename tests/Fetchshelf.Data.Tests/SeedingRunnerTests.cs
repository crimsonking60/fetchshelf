using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class SeedingRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-seeding-runner-tests-").FullName;
    private readonly List<string> _requests = [];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string DbPath => Path.Combine(_root, "test.db");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static QBittorrentTorrent Torrent(string hash, double seededHours, double addedDaysAgo, string state = "stalledUP", string tags = "", double progress = 1) => new()
    {
        Hash = hash,
        Name = hash,
        SavePath = "/downloads",
        State = state,
        Progress = progress,
        Tags = tags,
        SeedingTime = (long)TimeSpan.FromHours(seededHours).TotalSeconds,
        AddedOn = Now.AddDays(-addedDaysAgo).ToUnixTimeSeconds(),
    };

    /// <summary>A qBittorrent with these torrents, which records the tag and category changes asked of it.</summary>
    private QBittorrentClient Client(params QBittorrentTorrent[] torrents) =>
        new(new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("auth/login"))
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc; path=/");
                return login;
            }

            if (path.Contains("torrents/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
            }

            _requests.Add($"{path[(path.LastIndexOf('/') + 1)..]} {request.Content!.ReadAsStringAsync().Result}");
            return new HttpResponseMessage(HttpStatusCode.OK);
        })), new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));

    private static async Task SeedAsync(FetchshelfDbContext context, string id, Action<TorrentEntity>? configure = null)
    {
        await OrganizerTestSupport.SeedTorrentAsync(context, id, 1);
        var entity = await context.Torrents.SingleAsync(t => t.Id == id);
        configure?.Invoke(entity);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static (SeedingRunner Runner, Notifier Notifier) Runner(FetchshelfDbContext context, string? tag = null)
    {
        var notifier = new Notifier([new NotifyTarget("Phone", "https://x.test/a", NotificationFormat.Json, null, new HashSet<NotificationEvent> { NotificationEvent.UnsatisfiedNotSeeding })]);
        return (new SeedingRunner(context, new SeedingSettings { TagUnsatisfied = tag }, notifier, new Clock(Now)), notifier);
    }

    private static Dictionary<string, string?> States(FetchshelfDbContext context) =>
        context.Torrents.AsNoTracking().ToDictionary(t => t.Id, t => t.SeedingState);

    [Fact]
    public async Task RunAsync_EstimatesEachFinishedTorrent_AndLeavesTheRestAlone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        foreach (var id in new[] { "done", "short", "stopped", "late", "downloading", "elsewhere" })
        {
            await SeedAsync(context, id);
        }

        var (runner, notifier) = Runner(context);
        var client = Client(
            Torrent("done", 80, 10), Torrent("short", 10, 5), Torrent("stopped", 10, 5, state: "stoppedUP"),
            Torrent("late", 10, 31), Torrent("downloading", 0, 1, state: "downloading", progress: 0.5));

        var summary = await runner.RunAsync([(new QBittorrentConnection(), client)]);

        Assert.Equal(new SeedingRunSummary(4, 2, 1, 1, 0, 0), summary);
        var states = States(context);
        Assert.Equal("Satisfied", states["done"]);
        Assert.Equal("SeedingUnsatisfied", states["short"]);
        Assert.Equal("NotSeedingUnsatisfied", states["stopped"]);
        Assert.Equal("PastThePeriod", states["late"]);
        Assert.Null(states["downloading"]);
        Assert.Null(states["elsewhere"]);
        var stopped = context.Torrents.AsNoTracking().Single(t => t.Id == "stopped");
        Assert.Equal((36000L, Now.AddDays(-5), Now), (stopped.SeedingSeconds!.Value, stopped.SeedingSince!.Value, stopped.SeedingCheckedAt!.Value));
        Assert.True(notifier.Reader.TryRead(out var notification));
        Assert.Equal(NotificationEvent.UnsatisfiedNotSeeding, notification.Event);
        Assert.Equal("/torrents/stopped", notification.LinkPath);
        Assert.False(notifier.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RunAsync_NotSeedingAgain_IsNotNotifiedTwice()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "stopped", t => (t.SeedingState, t.SeedingSeconds, t.SeedingSince) = ("NotSeedingUnsatisfied", 36000, Now.AddDays(-5)));
        var (runner, notifier) = Runner(context);

        await runner.RunAsync([(new QBittorrentConnection(), Client(Torrent("stopped", 10, 5, state: "stoppedUP")))]);

        Assert.False(notifier.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RunAsync_GoneFromQBittorrent_IsNotSeeding_WithItsLastSeedingTime()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "gone", t => (t.SeedingState, t.SeedingSeconds, t.SeedingSince) = ("SeedingUnsatisfied", 36000, Now.AddDays(-5)));
        await SeedAsync(context, "gone-satisfied", t => (t.SeedingState, t.SeedingSeconds, t.SeedingSince) = ("Satisfied", 300000, Now.AddDays(-50)));
        var (runner, _) = Runner(context);

        await runner.RunAsync([(new QBittorrentConnection(), Client())]);

        Assert.Equal("NotSeedingUnsatisfied", States(context)["gone"]);
        Assert.Equal("Satisfied", States(context)["gone-satisfied"]);
    }

    [Fact]
    public async Task RunAsync_TagUnsatisfied_IsAddedAndRemoved_OnlyWhereThatChangesAnything()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        foreach (var id in new[] { "needs-tag", "has-tag", "satisfied-with-tag", "satisfied" })
        {
            await SeedAsync(context, id);
        }

        var (runner, _) = Runner(context, tag: "unsatisfied");

        await runner.RunAsync([(new QBittorrentConnection(), Client(
            Torrent("needs-tag", 10, 5), Torrent("has-tag", 10, 5, tags: "unsatisfied"),
            Torrent("satisfied-with-tag", 80, 5, tags: "books, unsatisfied"), Torrent("satisfied", 80, 5)))]);

        Assert.Equal(["addTags hashes=needs-tag&tags=unsatisfied", "removeTags hashes=satisfied-with-tag&tags=unsatisfied"], _requests);
    }

    [Fact]
    public async Task RunAsync_HeldBackOnCleaned_IsAppliedOnceSatisfied()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "retired", t => (t.SeedingState, t.OnCleanedHeld) = ("SeedingUnsatisfied", true));
        await SeedAsync(context, "still-short", t => (t.SeedingState, t.OnCleanedHeld) = ("SeedingUnsatisfied", true));
        var (runner, _) = Runner(context);
        var connection = new QBittorrentConnection { OnCleaned = new QBittorrentCategoryAndTags { Category = "cleaned" } };

        var summary = await runner.RunAsync([(connection, Client(Torrent("retired", 80, 10), Torrent("still-short", 10, 5)))]);

        Assert.Equal(1, summary.OnCleanedApplied);
        Assert.Equal(1, summary.NewlySatisfied);
        // Setting a category creates it first if qBittorrent doesn't have it.
        Assert.Contains("setCategory hashes=retired&category=cleaned", _requests);
        Assert.DoesNotContain(_requests, r => r.Contains("still-short", StringComparison.Ordinal));
        var torrents = context.Torrents.AsNoTracking().ToDictionary(t => t.Id);
        Assert.False(torrents["retired"].OnCleanedHeld);
        Assert.True(torrents["still-short"].OnCleanedHeld);
    }
}

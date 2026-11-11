using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// The real app, with a `[[notify]]` block pointing at a fake service: what the jobs, MAM and the event log tell it reaches
/// the service as the plan says, in the format the block asks for.
/// </summary>
public class NotificationWiringTests
{
    // No timers, so no job runs on its own while a test runs.
    private const string Config = """
        mam_id = "test-mam-id"
        search_interval = 0
        link_interval = 0
        unsat_buffer = 10
        web_url = "https://fetchshelf.example.net"

        [[notify]]
        name = "Home Assistant"
        url = "https://ha.example/api/webhook/secret"
        events = ["all"]
        """;

    private sealed class Service
    {
        private readonly ConcurrentQueue<JsonNode> _messages = new();

        public IReadOnlyList<JsonNode> Messages => [.. _messages];

        public IReadOnlyList<string> Events => [.. Messages.Select(m => m["event"]!.GetValue<string>())];

        public HttpResponseMessage Receive(HttpRequestMessage request)
        {
            _messages.Enqueue(JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public async Task WaitForAsync(int count)
        {
            for (var i = 0; i < 500 && _messages.Count < count; i++)
            {
                await Task.Delay(10);
            }
        }
    }

    private static HttpResponseMessage Account(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static FetchshelfWebAppFixture Fixture(Service service, Func<HttpRequestMessage, HttpResponseMessage>? mam = null) =>
        new(Config, mamHandler: mam ?? (request => request.RequestUri!.AbsolutePath.Contains("jsonLoad.php")
            ? Account(MamAccountFixture.Json)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }),
            notifyHandler: service.Receive, realJobRegistry: true);

    private static async Task StartAsync(FetchshelfWebAppFixture fixture) => await fixture.CreateClient().GetAsync("/Jobs");

    [Fact(Timeout = 30_000)]
    public async Task AJobThatFailsTwiceAndThenRecovers_SendsOneFailureAndOneRecovery()
    {
        var service = new Service();
        using var fixture = Fixture(service);
        await StartAsync(fixture);
        var registry = fixture.CreateScope().ServiceProvider.GetRequiredService<IJobRegistry>();
        var fail = true;
        registry.Register("Wiring job", _ => fail ? throw new InvalidOperationException("the connection to qBittorrent failed") : Task.CompletedTask);

        await registry.TriggerAsync("Wiring job");
        await registry.TriggerAsync("Wiring job");
        fail = false;
        await registry.TriggerAsync("Wiring job");
        await service.WaitForAsync(2);
        await Task.Delay(200); // nothing more may follow

        Assert.Equal(["job_failing", "job_recovered"], service.Events);
        Assert.Equal("Job Wiring job failed: the connection to qBittorrent failed", service.Messages[0]["message"]!.GetValue<string>());
        Assert.StartsWith("Job Wiring job works again, after 2 failed runs since", service.Messages[1]["message"]!.GetValue<string>());
        Assert.Equal("https://fetchshelf.example.net/Jobs/History?job=Wiring%20job", service.Messages[0]["url"]!.GetValue<string>());
    }

    [Fact(Timeout = 30_000)]
    public async Task MamRefusingMamId_SendsOneMessageNotOnePerCallOrJob()
    {
        var service = new Service();
        using var fixture = Fixture(service, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        await StartAsync(fixture);
        var scope = fixture.CreateScope().ServiceProvider;
        var mam = scope.GetRequiredService<MamClient>();
        var registry = scope.GetRequiredService<IJobRegistry>();
        registry.Register("MAM job A", async _ => await mam.SearchAsync(new MamSearchQuery()));
        registry.Register("MAM job B", async _ => await mam.SearchAsync(new MamSearchQuery()));

        await registry.TriggerAsync("MAM job A");
        await registry.TriggerAsync("MAM job B"); // paused: fails without reaching MAM
        await service.WaitForAsync(1);
        await Task.Delay(300);

        Assert.Equal(["mam_refused"], service.Events); // the jobs' own failures are covered by it: all-events block
        Assert.StartsWith("MAM refused mam_id (HTTP 403). MAM jobs are paused until", service.Messages[0]["message"]!.GetValue<string>());
    }

    [Fact(Timeout = 30_000)]
    public async Task ANewHitAndRun_IsSentOnceAndItsCountStoredInTheDatabase()
    {
        var service = new Service();
        using var fixture = Fixture(service, request => request.RequestUri!.AbsolutePath.Contains("jsonLoad.php")
            ? Account(MamAccountFixture.WithCounts(("inactHnr", 1)))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await StartAsync(fixture);
        var mam = fixture.CreateScope().ServiceProvider.GetRequiredService<MamClient>();

        await mam.GetAccountStateAsync();
        await mam.RefreshAccountStateAsync(); // the same count again
        await service.WaitForAsync(1);
        await Task.Delay(300);

        Assert.Equal(["hit_and_run"], service.Events);
        using var scope = fixture.CreateScope();
        var state = await scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().NotificationStates.AsNoTracking().SingleAsync();
        Assert.Equal(("hit_and_run.inactHnr", "1"), (state.Key, state.Value));
    }

    [Fact(Timeout = 30_000)]
    public async Task SelectionsThatWaitForBudget_AreToldOnceNotOnEveryDownloadRun()
    {
        var service = new Service();
        // 20 unsatisfied snatches of 20 allowed: no slot is free, so a queued selection waits.
        using var fixture = Fixture(service, request => request.RequestUri!.AbsolutePath.Contains("jsonLoad.php")
            ? Account(MamAccountFixture.Json.Replace("\"unsat\": {\"count\": 14,", "\"unsat\": {\"count\": 20,", StringComparison.Ordinal))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await StartAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            var metadata = OrganizerTestSupport.BuildMetadata();
            db.SelectedTorrents.Add(new SelectedTorrentEntity
            {
                MamId = 5,
                DownloadLink = "dl5",
                CostDecision = "Ratio",
                MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
                NormalizedTitleKey = "the way of kings",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var registry = fixture.CreateScope().ServiceProvider.GetRequiredService<IJobRegistry>();
        await registry.TriggerAsync("Download");
        await registry.TriggerAsync("Download");
        await service.WaitForAsync(1);
        await Task.Delay(300);

        Assert.Equal(["budget_spent"], service.Events);
        Assert.Equal(
            "1 selection waits: no unsatisfied-snatch slot is free (20 of 20 in use, unsat_buffer 10).",
            service.Messages[0]["message"]!.GetValue<string>());
        Assert.Contains("1 stay queued", registry.Get("Download")!.LastRun!.Summary);
    }

    private sealed class RecordingSink : INotificationSink
    {
        public List<Notification> Published { get; } = [];

        public void Publish(Notification notification) => Published.Add(notification);

        public bool Wants(NotificationEvent notificationEvent) => true;
    }

    [Fact]
    public async Task AGrabSavedThroughTheAppsEventStore_ReachesTheNotificationSink()
    {
        var sink = new RecordingSink();
        using var fixture = new FetchshelfWebAppFixture(
            Config, notifyHandler: _ => new HttpResponseMessage(HttpStatusCode.OK), configureServices: services =>
                services.AddSingleton<INotificationSink>(sink));
        await StartAsync(fixture);
        using var scope = fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<EventStore>().AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            MamId = 7,
            Kind = EventKind.Grabbed,
            GrabberName = "Autograb Freeleech",
            Cost = "GlobalFreeleech",
        });

        Assert.Equal("Grabbed torrent 7 (Autograb Freeleech, freeleech).", Assert.Single(sink.Published).Message);
    }
}

using System.Net;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.Tests;

public class NotificationDeliveryTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Sent(string Url, string? Authorization, string Body, IReadOnlyDictionary<string, string> Headers);

    /// <summary>A server that records what it is sent and answers as <c>answer</c> says for the number of the request.</summary>
    private sealed class FakeServer(Func<int, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        /// <summary>Waits until the server has been sent a request.</summary>
        public async Task WaitForRequestAsync()
        {
            for (var i = 0; i < 500 && Requests.Count == 0; i++)
            {
                await Task.Delay(10);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Sent(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
            return answer(Requests.Count);
        }
    }

    private sealed class ThrowingServer(Exception exception) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw exception;
        }
    }

    private static NotifyTarget Target(string name, string url, NotificationFormat format = NotificationFormat.Json, string? token = null, params NotificationEvent[] events) =>
        new(name, url, format, token, events.ToHashSet());

    private static Notification Grabbed() => new(
        NotificationEvent.Grabbed, "Grabbed A", "Grabbed A.", At, new Dictionary<string, object?> { ["mam_id"] = 1 }, "/torrents/abc");

    private static HttpResponseMessage Status(int status) => new((HttpStatusCode)status);

    private static NotificationDelivery Delivery(
        IReadOnlyList<NotifyTarget> targets, HttpMessageHandler handler, List<TimeSpan>? delays = null, ListLogger<NotificationDelivery>? logger = null) =>
        new(targets, () => new HttpClient(handler), webUrl: "https://fetchshelf.example.net", logger, (wait, _) =>
        {
            delays?.Add(wait);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task DeliverAsync_OnlyBlocksWantingTheEventGetIt()
    {
        var server = new FakeServer(_ => Status(200));
        var delivery = Delivery(
            [
                Target("Phone", "https://ntfy.test/phone", events: NotificationEvent.Grabbed),
                Target("Chat", "https://discord.test/chat", events: NotificationEvent.Linked),
                Target("Home", "https://ha.test/hook", events: [NotificationEvent.Grabbed, NotificationEvent.Linked]),
            ],
            server);

        await delivery.DeliverAsync(Grabbed());

        Assert.Equal(["https://ha.test/hook", "https://ntfy.test/phone"], server.Requests.Select(r => r.Url).Order());
    }

    [Fact]
    public async Task DeliverAsync_TheRequestIsTheBlocksFormatWithItsTokenAndTheLink()
    {
        var server = new FakeServer(_ => Status(200));
        var delivery = Delivery([Target("Phone", "https://ntfy.test/phone", NotificationFormat.Ntfy, "tk_abc", NotificationEvent.Grabbed)], server);

        await delivery.DeliverAsync(Grabbed());

        var sent = Assert.Single(server.Requests);
        Assert.Equal("Bearer tk_abc", sent.Authorization);
        Assert.Equal("Grabbed A.", sent.Body);
        Assert.Equal("https://fetchshelf.example.net/torrents/abc", sent.Headers["Click"]);
        Assert.Equal("Grabbed A", sent.Headers["Title"]);
    }

    [Fact]
    public async Task DeliverAsync_AJobFailingBecauseOfMam_IsNotSentToABlockThatWantsTheMamEventToo()
    {
        var server = new FakeServer(_ => Status(200));
        var delivery = Delivery(
            [
                Target("Both", "https://x.test/both", events: [NotificationEvent.JobFailing, NotificationEvent.MamRefused]),
                Target("Jobs only", "https://x.test/jobs", events: NotificationEvent.JobFailing),
            ],
            server);
        var failing = new Notification(NotificationEvent.JobFailing, "t", "m", At, SupersededBy: NotificationEvent.MamRefused);

        await delivery.DeliverAsync(failing);

        Assert.Equal("https://x.test/jobs", Assert.Single(server.Requests).Url);
    }

    [Fact]
    public async Task DeliverAsync_A500_IsTriedOnceMoreAfterTenSeconds()
    {
        var server = new FakeServer(n => Status(n == 1 ? 500 : 200));
        var delays = new List<TimeSpan>();
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)], server, delays, logger).DeliverAsync(Grabbed());

        Assert.Equal(2, server.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(10)], delays);
        Assert.Empty(logger.Messages(LogLevel.Warning));
    }

    [Fact]
    public async Task DeliverAsync_A500Twice_IsDroppedAndLoggedAsAWarning()
    {
        var server = new FakeServer(_ => Status(500));
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://x.test/secret-topic", events: NotificationEvent.Grabbed)], server, logger: logger).DeliverAsync(Grabbed());

        Assert.Equal(2, server.Requests.Count);
        var warning = Assert.Single(logger.Messages(LogLevel.Warning));
        Assert.Equal("Notify Phone: could not send grabbed: HTTP 500", warning);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    public async Task DeliverAsync_ARefusedTokenOrWrongAddress_IsNotTriedAgain(int status)
    {
        var server = new FakeServer(_ => Status(status));
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)], server, logger: logger).DeliverAsync(Grabbed());

        Assert.Single(server.Requests);
        Assert.Equal($"Notify Phone: could not send grabbed: HTTP {status}", Assert.Single(logger.Messages(LogLevel.Warning)));
    }

    [Fact]
    public async Task DeliverAsync_ATimeout_IsRetriedThenLoggedAndDropped()
    {
        var server = new ThrowingServer(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)], server, logger: logger).DeliverAsync(Grabbed());

        Assert.Equal(2, server.Requests);
        Assert.Equal("Notify Phone: could not send grabbed: no answer within the time limit", Assert.Single(logger.Messages(LogLevel.Warning)));
    }

    [Fact]
    public async Task DeliverAsync_AConnectionErrorThatNamesTheUrl_IsLoggedWithoutIt()
    {
        var server = new ThrowingServer(new HttpRequestException("Connection refused (https://ntfy.test/secret-topic-x7k2)"));
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://ntfy.test/secret-topic-x7k2", events: NotificationEvent.Grabbed)], server, logger: logger).DeliverAsync(Grabbed());

        var warning = Assert.Single(logger.Messages(LogLevel.Warning));
        Assert.DoesNotContain("x7k2", warning);
        Assert.Contains("Connection refused ((url))", warning);
    }

    [Fact]
    public async Task DeliverAsync_AFailureOfAnUnexpectedKind_IsLoggedNotThrown()
    {
        var server = new ThrowingServer(new InvalidCastException("something nobody expected"));
        var logger = new ListLogger<NotificationDelivery>();

        await Delivery([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)], server, logger: logger).DeliverAsync(Grabbed());

        Assert.Contains("something nobody expected", Assert.Single(logger.Messages(LogLevel.Warning)));
    }

    [Fact]
    public async Task DeliverAsync_OneSlowBlock_DoesNotHoldUpTheOthers()
    {
        var release = new TaskCompletionSource();
        var fast = new FakeServer(_ => Status(200));
        var slow = new SlowServer(release.Task);
        var delivery = new NotificationDelivery(
            [Target("Slow", "https://slow.test/a", events: NotificationEvent.Grabbed), Target("Fast", "https://fast.test/a", events: NotificationEvent.Grabbed)],
            () => new HttpClient(new RoutingHandler(request => request.RequestUri!.Host == "slow.test" ? slow : fast)),
            null);

        var delivering = delivery.DeliverAsync(Grabbed());
        await fast.WaitForRequestAsync();

        Assert.False(delivering.IsCompleted);
        release.SetResult();
        await delivering;
    }

    [Fact]
    public async Task SendTestAsync_SendsOnceWhateverTheBlockWantsAndSaysTheStatus()
    {
        var server = new FakeServer(_ => Status(204));
        var delays = new List<TimeSpan>();
        var target = Target("Phone", "https://x.test/a", events: NotificationEvent.Linked);

        var result = await Delivery([target], server, delays).SendTestAsync(target);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 204", result.Describe());
        Assert.Contains("Fetchshelf test message from", Assert.Single(server.Requests).Body);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task SendTestAsync_AFailure_IsNotRetriedAndSaysWhatWentWrong()
    {
        var server = new FakeServer(_ => Status(500));
        var target = Target("Phone", "https://x.test/a", events: NotificationEvent.Linked);

        var result = await Delivery([target], server).SendTestAsync(target);

        Assert.False(result.Succeeded);
        Assert.Equal("HTTP 500", result.Describe());
        Assert.Single(server.Requests);
    }

    [Fact]
    public void Notifier_OnlyQueuesWhatSomeBlockWouldReceive()
    {
        var notifier = new Notifier([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)]);

        notifier.Publish(Grabbed());
        notifier.Publish(new Notification(NotificationEvent.Linked, "t", "m", At));

        Assert.True(notifier.Reader.TryRead(out var queued));
        Assert.Equal(NotificationEvent.Grabbed, queued!.Event);
        Assert.False(notifier.Reader.TryRead(out _));
        Assert.True(notifier.Wants(NotificationEvent.Grabbed));
        Assert.False(notifier.Wants(NotificationEvent.HitAndRun));
    }

    [Fact]
    public void Notifier_WithoutBlocks_TakesNothing()
    {
        var notifier = new Notifier([]);

        notifier.Publish(Grabbed());

        Assert.False(notifier.Reader.TryRead(out _));
    }

    [Fact]
    public void Notifier_AFullQueue_DropsTheOldestAndSaysSo()
    {
        var logger = new ListLogger<Notifier>();
        var notifier = new Notifier([Target("Phone", "https://x.test/a", events: NotificationEvent.Grabbed)], logger);

        for (var i = 0; i < Notifier.QueueSize + 5; i++)
        {
            notifier.Publish(Grabbed() with { Title = $"Grabbed {i}" });
        }

        var queued = new List<string>();
        while (notifier.Reader.TryRead(out var notification))
        {
            queued.Add(notification.Title);
        }

        Assert.Equal(Notifier.QueueSize, queued.Count);
        Assert.Equal("Grabbed 5", queued[0]);
        Assert.Equal($"Grabbed {Notifier.QueueSize + 4}", queued[^1]);
        Assert.Equal(5, logger.Messages(LogLevel.Warning).Count);
        Assert.Contains("Grabbed 0", logger.Messages(LogLevel.Warning)[0]);
    }

    private sealed class SlowServer(Task until) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await until;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpMessageHandler> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new HttpMessageInvoker(route(request), disposeHandler: false).SendAsync(request, cancellationToken);
    }
}

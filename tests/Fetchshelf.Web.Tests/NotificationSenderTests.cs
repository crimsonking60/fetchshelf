using System.Collections.Concurrent;
using System.Net;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Web.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Web.Tests;

public class NotificationSenderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class Server : HttpMessageHandler
    {
        private readonly List<string> _bodies = [];

        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (_bodies)
                {
                    return [.. _bodies];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (_bodies)
            {
                _bodies.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public async Task WaitForAsync(int count)
        {
            for (var i = 0; i < 500 && Bodies.Count < count; i++)
            {
                await Task.Delay(10);
            }
        }
    }

    private static (NotificationSender Sender, Notifier Notifier, Server Server, CountingFakeTime Time) Create()
    {
        var time = new CountingFakeTime(Start);
        var target = new NotifyTarget("Phone", "https://x.test/a", NotificationFormat.Ntfy, null, NotificationEvents.All.ToHashSet());
        var notifier = new Notifier([target]);
        var server = new Server();
        var delivery = new NotificationDelivery([target], () => new HttpClient(server), null, delay: (_, _) => Task.CompletedTask);
        return (new NotificationSender(notifier, delivery, time, NullLogger<NotificationSender>.Instance), notifier, server, time);
    }

    private static Notification Grabbed(string item) =>
        new(NotificationEvent.Grabbed, $"Grabbed {item}", $"Grabbed {item}.", Start, Item: item);

    /// <summary>A service that answers only when released, like one that is down and times out.</summary>
    private sealed class StuckServer : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class RoutingHandler(HttpMessageHandler stuck, HttpMessageHandler healthy) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new HttpMessageInvoker(request.RequestUri!.Host == "down.test" ? stuck : healthy, disposeHandler: false).SendAsync(request, cancellationToken);
    }

    private sealed class CollectingLogger : ILogger<NotificationSender>
    {
        public ConcurrentQueue<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Enqueue(formatter(state, exception));
            }
        }
    }

    private static (NotificationSender Sender, Notifier Notifier, StuckServer Down, Server Healthy, CountingFakeTime Time, CollectingLogger Logger) CreateTwo()
    {
        var time = new CountingFakeTime(Start);
        NotifyTarget[] targets =
        [
            new("Down", "https://down.test/a", NotificationFormat.Ntfy, null, NotificationEvents.All.ToHashSet()),
            new("Healthy", "https://healthy.test/a", NotificationFormat.Ntfy, null, NotificationEvents.All.ToHashSet()),
        ];
        var notifier = new Notifier(targets);
        var (down, healthy) = (new StuckServer(), new Server());
        var delivery = new NotificationDelivery(targets, () => new HttpClient(new RoutingHandler(down, healthy)), null, delay: (_, _) => Task.CompletedTask);
        var logger = new CollectingLogger();
        return (new NotificationSender(notifier, delivery, time, logger), notifier, down, healthy, time, logger);
    }

    private static Notification Failing(string job) => new(NotificationEvent.JobFailing, $"Job {job} is failing", $"Job {job} failed.", Start);

    [Fact(Timeout = 20_000)]
    public async Task ABlockThatIsDown_HoldsUpOnlyItsOwnMessages()
    {
        var (sender, notifier, down, healthy, _, _) = CreateTwo();
        await sender.StartAsync(CancellationToken.None);

        notifier.Publish(Failing("A"));
        notifier.Publish(Failing("B"));
        notifier.Publish(Failing("C"));
        await healthy.WaitForAsync(3);

        Assert.Equal(["Job A failed.", "Job B failed.", "Job C failed."], healthy.Bodies);
        Assert.Equal(1, Volatile.Read(ref down.Requests)); // still stuck on the first
        down.Release.SetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(3, Volatile.Read(ref down.Requests)); // and caught up once it answered
    }

    [Fact(Timeout = 20_000)]
    public async Task Stopping_GivesQueuedMessagesTheGraceToBeSent_TheOneBeingSentIncluded()
    {
        var (sender, notifier, down, healthy, _, logger) = CreateTwo();
        await sender.StartAsync(CancellationToken.None);
        notifier.Publish(Failing("A"));
        notifier.Publish(Failing("B"));
        await healthy.WaitForAsync(2);

        var stopping = sender.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        down.Release.SetResult(); // the service answers within the grace
        await stopping;

        Assert.Equal(2, Volatile.Read(ref down.Requests));
        Assert.Empty(logger.Warnings);
    }

    [Fact(Timeout = 20_000)]
    public async Task Stopping_WhatCannotBeSentWithinTheGrace_IsCountedInTheLog()
    {
        var (sender, notifier, _, healthy, time, logger) = CreateTwo();
        await sender.StartAsync(CancellationToken.None);
        notifier.Publish(Failing("A"));
        notifier.Publish(Failing("B"));
        await healthy.WaitForAsync(2);

        var stopping = sender.StopAsync(CancellationToken.None);
        await time.WaitForTimersAsync(1); // the grace's timer
        time.Advance(NotificationSender.FlushOnStop);
        await stopping;

        // A was cut off mid-send, B never left the queue.
        Assert.Contains("2 notifications could not be sent before Fetchshelf stopped", logger.Warnings);
    }

    [Fact(Timeout = 20_000)]
    public async Task FourGrabsCloseTogether_AreOneMessageSentOnceTheyHaveGoneQuiet()
    {
        var (sender, notifier, server, time) = Create();
        await sender.StartAsync(CancellationToken.None);

        foreach (var item in new[] { "A", "B", "C", "D" })
        {
            notifier.Publish(Grabbed(item));
        }

        await time.WaitForTimersAsync(1);
        time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(100);
        Assert.Empty(server.Bodies);

        time.Advance(TimeSpan.FromSeconds(1));
        await server.WaitForAsync(1);

        Assert.Equal("Grabbed 4 torrents: A, B, C and 1 more.", Assert.Single(server.Bodies));
        await sender.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 20_000)]
    public async Task AJobFailing_IsSentAtOnceAndNotHeldWithTheGrabs()
    {
        var (sender, notifier, server, _) = Create();
        await sender.StartAsync(CancellationToken.None);

        notifier.Publish(new Notification(NotificationEvent.JobFailing, "Job Link is failing", "Job Link failed: boom", Start));
        await server.WaitForAsync(1);

        Assert.Equal("Job Link failed: boom", Assert.Single(server.Bodies));
        await sender.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 20_000)]
    public async Task Stopping_SendsAHeldBurstInsteadOfLosingIt()
    {
        var (sender, notifier, server, time) = Create();
        await sender.StartAsync(CancellationToken.None);
        notifier.Publish(Grabbed("A"));
        notifier.Publish(Grabbed("B"));
        await time.WaitForTimersAsync(1);

        await sender.StopAsync(CancellationToken.None);

        Assert.Equal("Grabbed 2 torrents: A and B.", Assert.Single(server.Bodies));
    }

    [Fact(Timeout = 20_000)]
    public async Task AGrabFortySecondsLater_IsItsOwnMessage()
    {
        var (sender, notifier, server, time) = Create();
        await sender.StartAsync(CancellationToken.None);
        notifier.Publish(Grabbed("A"));
        await time.WaitForTimersAsync(1);
        time.Advance(TimeSpan.FromSeconds(30));
        await server.WaitForAsync(1);

        notifier.Publish(Grabbed("B"));
        await time.WaitForTimersAsync(3);
        time.Advance(TimeSpan.FromSeconds(30));
        await server.WaitForAsync(2);

        Assert.Equal(["Grabbed A.", "Grabbed B."], server.Bodies);
        await sender.StopAsync(CancellationToken.None);
    }
}

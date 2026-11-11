using System.Net;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Web.Notifications;
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

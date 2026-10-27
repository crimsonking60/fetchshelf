using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientSearchPolitenessTests
{
    private const string EmptyResultJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    [Fact]
    public async Task SearchAsync_ConsecutivePages_RequestsAtLeast400MsDelayBeforeSecondSend()
    {
        var timeProvider = new ManualTimeProvider();
        var requestedDelays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (wait, _) =>
        {
            requestedDelays.Add(wait);
            timeProvider.Advance(wait);
            return Task.CompletedTask;
        };
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) })),
            new MamClientOptions { MamId = "cookie" },
            timeProvider,
            fakeDelay);

        await client.SearchAsync(new MamSearchQuery());
        timeProvider.Advance(TimeSpan.FromMilliseconds(100)); // only 100ms passed before the next page
        await client.SearchAsync(new MamSearchQuery());

        var delay = Assert.Single(requestedDelays);
        Assert.True(delay >= TimeSpan.FromMilliseconds(300), $"Expected a delay topping up to 400ms total, requested {delay}.");
    }

    [Fact]
    public async Task SearchAsync_SecondPageAfterWindowAlreadyElapsed_RequestsNoDelay()
    {
        var timeProvider = new ManualTimeProvider();
        var requestedDelays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (wait, _) =>
        {
            requestedDelays.Add(wait);
            return Task.CompletedTask;
        };
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) })),
            new MamClientOptions { MamId = "cookie" },
            timeProvider,
            fakeDelay);

        await client.SearchAsync(new MamSearchQuery());
        timeProvider.Advance(TimeSpan.FromMilliseconds(500)); // already well past 400ms
        await client.SearchAsync(new MamSearchQuery());

        Assert.Empty(requestedDelays);
    }
}

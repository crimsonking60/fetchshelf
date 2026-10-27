namespace Fetchshelf.Clients.QBittorrent.Tests;

public class QBittorrentClientFactoryTests
{
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
    }

    [Fact]
    public void Create_TwoDifferentConnectionEntries_YieldsIndependentlyConfiguredClients()
    {
        var factory = new QBittorrentClientFactory(new FakeHttpClientFactory());
        var optionsA = new QBittorrentClientOptions("http://host-a:8080", "user-a", "pass-a");
        var optionsB = new QBittorrentClientOptions("http://host-b:9090", "user-b", "pass-b");

        var clientA = factory.Create(optionsA);
        var clientB = factory.Create(optionsB);

        Assert.NotSame(clientA, clientB);
        Assert.Equal(optionsA, clientA.Options);
        Assert.Equal(optionsB, clientB.Options);
    }
}

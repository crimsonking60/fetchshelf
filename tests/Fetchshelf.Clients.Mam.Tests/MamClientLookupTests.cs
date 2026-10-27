using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientLookupTests
{
    private const string SingleResultJson = """
        {
            "perpage": 1,
            "start": 0,
            "total": 1,
            "found": 1,
            "data": [
                {
                    "id": 123456,
                    "title": "Dune",
                    "author_info": "{\"111\":\"Frank Herbert\"}",
                    "narrator_info": "{}",
                    "series_info": "{}",
                    "isbn": "9780441013593",
                    "description": "A desert planet.",
                    "mediatype": 1,
                    "main_cat": 13,
                    "maincat": 1,
                    "category": 39,
                    "categories": "[]",
                    "language": 1,
                    "browseflags": 0,
                    "filetype": "m4b",
                    "numfiles": 12,
                    "size": "1.23 GiB",
                    "tags": "sci-fi",
                    "owner": 999,
                    "owner_name": "uploader1",
                    "added": "2020-05-01 12:34:56",
                    "seeders": 10,
                    "leechers": 2,
                    "times_completed": 50,
                    "free": true,
                    "personal_freeleech": false,
                    "fl_vip": false,
                    "vip": false,
                    "vip_expire": 0,
                    "dl": "abcdef0123456789"
                }
            ]
        }
        """;

    private const string EmptyResultJson = """{"error": "Nothing returned, out of 0"}""";

    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" });

    [Fact]
    public async Task LookupAsync_ByMamId_ReturnsCleanedMetadata()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SingleResultJson) });

        var metadata = await client.LookupAsync(123456L);

        Assert.NotNull(metadata);
        Assert.Equal(123456, metadata!.MamId);
        Assert.Equal("Dune", metadata.Title);
    }

    [Fact]
    public async Task LookupAsync_ByInfoHash_ReturnsCleanedMetadata()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SingleResultJson) });

        var metadata = await client.LookupAsync("abcdef0123456789");

        Assert.NotNull(metadata);
        Assert.Equal(123456, metadata!.MamId);
    }

    [Fact]
    public async Task LookupAsync_ByMamId_NotFound_ReturnsNullRatherThanThrowing()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) });

        var metadata = await client.LookupAsync(999999L);

        Assert.Null(metadata);
    }

    [Fact]
    public async Task LookupAsync_ByInfoHash_NotFound_ReturnsNullRatherThanThrowing()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) });

        var metadata = await client.LookupAsync("unknown-hash");

        Assert.Null(metadata);
    }
}

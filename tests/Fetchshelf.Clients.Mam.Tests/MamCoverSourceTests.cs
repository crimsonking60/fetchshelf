using System.Net;
using System.Net.Http.Headers;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamCoverSourceTests
{
    private static string SearchJson(string? posterType) => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 123456, "title": "Dune", "author_info": "{\"111\":\"Frank Herbert\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "1 GiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-11-06 05:42:56",
                    "seeders": 1, "leechers": 0, "times_completed": 1, "free": false, "personal_freeleech": false,
                    "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "abc"
                    {{(posterType is null ? "" : $", \"poster_type\": \"{posterType}\"")}}
                }
            ]
        }
        """;

    private static (MamCoverSource Source, List<string> Requested) Create(string searchJson)
    {
        var requested = new List<string>();
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                requested.Add(request.RequestUri!.ToString());
                if (request.RequestUri.AbsolutePath.Contains("/t/p/"))
                {
                    var content = new ByteArrayContent([1, 2, 3]);
                    content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchJson) };
            })),
            new MamClientOptions { MamId = "cookie" },
            delay: (_, _) => Task.CompletedTask);
        return (new MamCoverSource(client, new MamAccessGate(), new MamClientOptions { MamId = "cookie" }), requested);
    }

    [Fact]
    public async Task FetchAsync_LooksTheTorrentUpThenFetchesItsPoster()
    {
        var (source, requested) = Create(SearchJson("image/jpeg"));

        var image = await source.FetchAsync(123456);

        Assert.Equal([1, 2, 3], image!.Bytes);
        Assert.Equal(2, requested.Count);
        Assert.Contains("loadSearchJSONbasic", requested[0]);
        Assert.Equal("https://cdn.myanonamouse.net/t/p/1793943776/large/123456.jpeg", requested[1]);
    }

    [Fact]
    public async Task FetchAsync_TorrentWithoutACover_FetchesNoImage()
    {
        var (source, requested) = Create(SearchJson(posterType: null));

        Assert.Null(await source.FetchAsync(123456));
        Assert.Single(requested);
    }

    [Fact]
    public async Task FetchAsync_GateHeldByAnotherJob_WaitsBeforeAskingMam()
    {
        var requested = 0;
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ =>
            {
                requested++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"error":"Nothing returned, out of 0"}""") };
            })),
            new MamClientOptions { MamId = "cookie" },
            delay: (_, _) => Task.CompletedTask);
        var gate = new MamAccessGate();
        var otherJob = await gate.AcquireAsync("another job");

        var fetch = new MamCoverSource(client, gate, new MamClientOptions { MamId = "cookie" }).FetchAsync(1);

        Assert.False(fetch.IsCompleted);
        Assert.Equal(0, requested);
        otherJob.Dispose();
        Assert.Null(await fetch);
        Assert.Equal(1, requested);
    }
}

using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;

namespace Fetchshelf.Data.AutoDownloader.Tests;

internal static class DownloadTestSupport
{
    public const string DefaultAccountStateJson = """
        {
            "ratio": 5.0,
            "uploaded_bytes": 1000000,
            "downloaded_bytes": 100,
            "wedges": 10,
            "uid": 1,
            "snatch_summary": { "unsat": { "count": 0, "limit": 20 } }
        }
        """;

    public static string TorrentRecordJson(
        long mamId, string downloadHash, bool isFree = false, bool isVip = false, bool isPersonalFreeleech = false,
        string title = "T", string authorName = "A") => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{authorName}}\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": {{(isFree ? "true" : "false")}}, "personal_freeleech": {{(isPersonalFreeleech ? "true" : "false")}},
                    "fl_vip": false, "vip": {{(isVip ? "true" : "false")}},
                    "vip_expire": 0, "dl": "{{downloadHash}}"
                }
            ]
        }
        """;

    public static MamClient CreateMamClient(
        string accountStateJson, Func<HttpRequestMessage, HttpResponseMessage?> router)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var routed = router(request);
            if (routed is not null)
            {
                return routed;
            }

            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(accountStateJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        return new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
    }

    public static QBittorrentClient CreateQBittorrentClient(List<QBittorrentTorrent> torrents, List<byte[]> addedTorrentBytes)
    {
        var options = new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin");
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("auth/login"))
            {
                var loginResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                loginResponse.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
                return loginResponse;
            }

            if (path.Contains("torrents/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
            }

            if (path.Contains("torrents/add"))
            {
                addedTorrentBytes.Add(ExtractTorrentBytes(request));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        return new QBittorrentClient(new HttpClient(handler), options);
    }

    private static byte[] ExtractTorrentBytes(HttpRequestMessage request)
    {
        var multipart = (MultipartFormDataContent)request.Content!;
        foreach (var part in multipart)
        {
            var name = part.Headers.ContentDisposition?.Name?.Trim('"');
            if (name == "torrents")
            {
                return part.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
        }

        return [];
    }
}

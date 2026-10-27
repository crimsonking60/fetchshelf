using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Proves the library organizer's link run now receives real metadata from MaM through
/// <see cref="MamMetadataLookup"/>/<see cref="MamClient"/>, where previously every lookup missed
/// with <c>NullMetadataLookup</c> (Fetchshelf-spec.md §8.2 step 4).
/// </summary>
public sealed class MamMetadataLookupCompositionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-mam-lookup-composition-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string LibraryDir => Path.Combine(_root, "library");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private const string SearchResponseJson = """
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
                    "numfiles": 1,
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
                    "dl": "hash1"
                }
            ]
        }
        """;

    [Fact]
    public async Task RunAsync_UntrackedTorrent_FindsAndLinksViaRealMamMetadataLookup()
    {
        Directory.CreateDirectory(DownloadsDir);
        File.WriteAllText(Path.Combine(DownloadsDir, "book.m4b"), "data");

        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var mamClient = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResponseJson) })),
            new MamClientOptions { MamId = "configured-mam-id" });
        var lookup = new MamMetadataLookup(mamClient);
        var libraries = new List<Library> { new() { LibraryDir = LibraryDir, Category = "audiobooks", Method = LibraryLinkMethod.Hardlink } };
        var runner = new LinkRunner(context, new EventStore(context), lookup, libraries, ["m4b"], ["epub"], excludeNarratorInLibraryDir: false);
        var connection = new QBittorrentConnection();
        var torrent = new QBittorrentTorrent
        {
            Hash = "hash1",
            Name = "Some Torrent",
            SavePath = DownloadsDir,
            Category = "audiobooks",
            State = "uploading",
            Progress = 1.0,
        };
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(connection, client);

        var saved = await context.Torrents.SingleAsync();
        Assert.Equal("hash1", saved.Id);
        Assert.NotNull(saved.LibraryPath);
        Assert.True(File.Exists(Path.Combine(saved.LibraryPath!, "book.m4b")));
        Assert.Equal(EventKind.Linked, (await context.Events.SingleAsync()).Kind);
    }
}

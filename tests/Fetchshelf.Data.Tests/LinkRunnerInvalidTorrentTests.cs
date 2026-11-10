using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>The Link job and `on_invalid_torrent`, and qBittorrent updates of a torrent marked removed from MAM.</summary>
public sealed class LinkRunnerInvalidTorrentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-link-invalid-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b"],
        EbookTypes = ["epub"],
        MusicTypes = ["mp3"],
        RadioTypes = ["mp3"],
    };

    private LinkRunner CreateRunner(FetchshelfDbContext context, IMetadataLookup lookup) =>
        new(context, new EventStore(context), lookup,
            [new Library { LibraryDir = Path.Combine(_root, "library"), Category = "audiobooks", Method = LibraryLinkMethod.Hardlink }],
            Formats, excludeNarratorInLibraryDir: false);

    private sealed class UnreadableLookup : IMetadataLookup
    {
        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
            throw new UnrecognizedReferenceDataException("media type", 17);
    }

    /// <summary>A qBittorrent with one finished torrent, recording the changes asked of it.</summary>
    private (QBittorrentClient Client, List<string> Changes) RecordingClient(string state = "uploading")
    {
        var changes = new List<string>();
        var torrent = new QBittorrentTorrent { Hash = "hash1", Name = "Some Torrent", SavePath = _root, Category = "audiobooks", State = state, Progress = 1.0 };
        var client = new QBittorrentClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("torrents/info"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { torrent }) };
                }

                if (path.Contains("torrents/files"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<QBittorrentFile>()) };
                }

                if (path.Contains("setCategory") || path.Contains("addTags") || path.Contains("createCategory"))
                {
                    changes.Add($"{path.Split('/')[^1]}: {Uri.UnescapeDataString(request.Content!.ReadAsStringAsync().Result)}");
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            })),
            new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));
        return (client, changes);
    }

    private static QBittorrentConnection WithOnInvalidTorrent() => new()
    {
        OnInvalidTorrent = new QBittorrentCategoryAndTags { Category = "invalid", Tags = ["fetchshelf-invalid"] },
    };

    [Fact]
    public async Task RunAsync_UnreadableDetailsWithOnInvalidTorrent_AppliesItAndSaysSoInTheError()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var (client, changes) = RecordingClient();

        var summary = await CreateRunner(context, new UnreadableLookup()).RunAsync(WithOnInvalidTorrent(), client);

        Assert.Equal(new LinkRunSummary(Linked: 0, Failed: 1), summary);
        Assert.Equal(
            [
                "createCategory: category=invalid&savePath=",
                "setCategory: hashes=hash1&category=invalid",
                "addTags: hashes=hash1&tags=fetchshelf-invalid",
            ],
            changes);
        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal(
            "MAM's details for this torrent can't be read (unrecognized media type: '17'); "
            + "applied on_invalid_torrent: category invalid, tag fetchshelf-invalid.",
            error.ErrorText);
        Assert.Empty(context.Torrents);
    }

    [Fact]
    public async Task RunAsync_UnreadableDetailsWithoutOnInvalidTorrent_RecordsTheErrorAndChangesNothing()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var (client, changes) = RecordingClient();

        await CreateRunner(context, new UnreadableLookup()).RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(changes);
        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal("Unrecognized media type: '17' is not a known reference value.", error.ErrorText);
    }

    [Fact]
    public async Task RunAsync_TorrentMamDoesNotHave_IsNotTouchedByOnInvalidTorrent()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var (client, changes) = RecordingClient();

        await CreateRunner(context, new FakeMetadataLookup(result: null)).RunAsync(WithOnInvalidTorrent(), client);

        Assert.Empty(changes);
        Assert.Equal(LinkRunner.NotOnMamError, (await context.ErroredTorrents.SingleAsync()).ErrorText);
    }

    [Fact]
    public async Task RunAsync_TorrentRemovedFromMam_KeepsTheMarkWhenItsStatusIsUpdated()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var stored = await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        var removedAt = new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        stored.ClientStatus = "stoppedUP";
        stored.RemovedFromMamAt = removedAt;
        stored.LibraryPath = Path.Combine(_root, "library", "Author", "Title");
        await context.SaveChangesAsync();
        var (client, _) = RecordingClient(state: "uploading");

        await CreateRunner(context, new FakeMetadataLookup()).RunAsync(new QBittorrentConnection(), client);

        context.ChangeTracker.Clear();
        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("uploading", torrent.ClientStatus);
        Assert.Equal(removedAt, torrent.RemovedFromMamAt);
    }
}

using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class DownloadProgressCheckerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-download-progress-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static QBittorrentTorrent InClient(string hash, string state, double progress) => new()
    {
        Hash = hash,
        Name = "Book",
        SavePath = "/downloads",
        State = state,
        Progress = progress,
    };

    private static async Task SeedAsync(
        FetchshelfDbContext context, string id, string? clientStatus = null, DateTimeOffset? addedAt = null, string? libraryPath = null)
    {
        var metadata = OrganizerTestSupport.BuildMetadata(title: $"Book {id}");
        context.Torrents.Add(new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = addedAt ?? Now.AddMinutes(-5),
            ClientStatus = clientStatus,
            LibraryPath = libraryPath,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>A qBittorrent with these torrents, noting each request that asks about torrents.</summary>
    private static (QBittorrentClient Client, List<string> Asked) Counting(params QBittorrentTorrent[] torrents)
    {
        var asked = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("auth/login"))
            {
                var login = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
                return login;
            }

            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            asked.Add(query);
            var hashes = query.Split("hashes=", 2).ElementAtOrDefault(1)?.Split('|') ?? [];
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(torrents.Where(t => hashes.Contains(t.Hash)).ToList()),
            };
        });
        return (new QBittorrentClient(new HttpClient(handler), new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin")), asked);
    }

    [Fact]
    public async Task Downloads_AreAskedAboutInOneRequest_AndTheirStateIsStored()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-a");
        await SeedAsync(context, "hash-b", clientStatus: "downloading");
        var (client, asked) = Counting(InClient("hash-a", "stalledUP", 1.0), InClient("hash-b", "downloading", 0.5));

        var result = await new DownloadProgressChecker(context, [client], new Clock(Now)).CheckAsync();

        Assert.Equal(new DownloadProgressResult(1, 1), result);
        Assert.Single(asked);
        var states = context.Torrents.AsNoTracking().ToDictionary(t => t.Id, t => t.ClientStatus);
        Assert.Equal("stalledUP", states["hash-a"]);
        Assert.Equal("downloading", states["hash-b"]);
    }

    [Fact]
    public async Task Linked_Finished_MetadataOnly_AndOldDownloads_AreLeftAlone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-linked", libraryPath: "/library/Book");
        await SeedAsync(context, "hash-seeding", clientStatus: "stalledUP");
        await SeedAsync(context, Fetchshelf.Core.AutoDownloader.MetadataOnlyTorrentId.For(1));
        await SeedAsync(context, "hash-old", addedAt: Now - DownloadProgressChecker.WatchFor - TimeSpan.FromMinutes(1));
        var (client, asked) = Counting();

        var result = await new DownloadProgressChecker(context, [client], new Clock(Now)).CheckAsync();

        Assert.Equal(new DownloadProgressResult(0, 0), result);
        Assert.Empty(asked); // nothing to ask about, so qBittorrent isn't asked
    }

    [Fact]
    public async Task ATorrentBeingCheckedAfterItWasAdded_IsFollowedOn_UntilItFinishes()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-a");
        var (checking, _) = Counting(InClient("hash-a", "checkingResumeData", 0));
        var (seeding, _) = Counting(InClient("hash-a", "stalledUP", 1.0));

        var first = await new DownloadProgressChecker(context, [checking], new Clock(Now)).CheckAsync();
        var second = await new DownloadProgressChecker(context, [seeding], new Clock(Now)).CheckAsync();

        Assert.Equal(new DownloadProgressResult(1, 0), first);
        Assert.Equal(new DownloadProgressResult(0, 1), second);
        Assert.Equal("stalledUP", context.Torrents.AsNoTracking().Single().ClientStatus);
    }

    [Fact]
    public async Task ATorrentAtFullProgressNotYetInAFinishedState_IsNotFinishedYet()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-a");
        var (client, _) = Counting(InClient("hash-a", "moving", 1.0));

        var result = await new DownloadProgressChecker(context, [client], new Clock(Now)).CheckAsync();

        Assert.Equal(new DownloadProgressResult(1, 0), result);
    }

    [Fact]
    public async Task AConnectionThatFails_IsReported_AndTheOthersAreStillAsked()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-a");
        var unreachable = new QBittorrentClient(
            new HttpClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"))),
            new QBittorrentClientOptions("http://down:8080", "admin", "pw"));
        var (client, _) = Counting(InClient("hash-a", "uploading", 1.0));

        var result = await new DownloadProgressChecker(context, [unreachable, client], new Clock(Now)).CheckAsync();

        Assert.Equal(1, result.Finished);
        Assert.Equal("Connection refused", result.Error);
        Assert.Equal("uploading", context.Torrents.AsNoTracking().Single().ClientStatus);
    }
}

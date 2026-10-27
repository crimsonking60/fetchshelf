using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

internal static class OrganizerTestSupport
{
    /// <summary>A minimal fake qBittorrent WebUI API v2 server: logs in, reports one torrent with <paramref name="hash"/>, and accepts any mutating call.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> FakeQBittorrentServer(
        string hash, string savePath = "/downloads", IReadOnlyList<string>? fileNames = null) => request =>
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
            var torrents = new List<QBittorrentTorrent>
            {
                new() { Hash = hash, Name = "Book", SavePath = savePath, Category = "audiobooks", Tags = "a,b", State = "uploading", Progress = 1.0 },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
        }

        if (path.Contains("torrents/files"))
        {
            var files = (fileNames ?? []).Select(name => new QBittorrentFile { Name = name, Size = 4 }).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(files) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    };
    public static TorrentMetadata BuildMetadata(
        string title = "The Way of Kings", IReadOnlyList<string>? authors = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = Language.English,
            Title = title,
            Authors = authors ?? ["Brandon Sanderson"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        };

    public static async Task<TorrentEntity> SeedTorrentAsync(
        FetchshelfWebAppFixture fixture, string id, TorrentMetadata? metadata = null,
        string? libraryPath = null, DateTimeOffset? createdAt = null)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize((metadata ?? BuildMetadata()).Title),
            Title = (metadata ?? BuildMetadata()).Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata ?? BuildMetadata()),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            LibraryPath = libraryPath,
        };
        dbContext.Torrents.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    public static async Task SeedEventAsync(FetchshelfWebAppFixture fixture, EventEntity @event)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync();
    }
}

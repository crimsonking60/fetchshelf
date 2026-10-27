using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

internal static class OrganizerTestSupport
{
    public static FetchshelfDbContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        var context = new FetchshelfDbContext(options);
        DatabaseMigrator.Migrate(context);
        return context;
    }

    public static QBittorrentClient CreateQBittorrentClient(
        IReadOnlyList<QBittorrentTorrent> torrents, IReadOnlyDictionary<string, List<QBittorrentFile>> filesByHash)
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

            if (path.Contains("torrents/files"))
            {
                var hash = ExtractQueryParam(request.RequestUri.Query, "hash")!;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(filesByHash.GetValueOrDefault(hash, [])),
                };
            }

            if (path.Contains("torrents/info"))
            {
                var hashesParam = ExtractQueryParam(request.RequestUri.Query, "hashes");
                var result = hashesParam is null ? torrents : torrents.Where(t => t.Hash == hashesParam).ToList();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        return new QBittorrentClient(new HttpClient(handler), options);
    }

    private static string? ExtractQueryParam(string query, string name) => query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .Where(parts => parts[0] == name)
        .Select(parts => Uri.UnescapeDataString(parts[1]))
        .FirstOrDefault();

    public static TorrentMetadata BuildMetadata(
        string title = "The Way of Kings",
        IReadOnlyList<string>? authors = null,
        Language language = Language.English) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = language,
            Title = title,
            Authors = authors ?? ["Brandon Sanderson"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        };

    public static async Task<TorrentEntity> SeedTorrentAsync(FetchshelfDbContext context, string id, long mamId)
    {
        var metadata = BuildMetadata(title: $"Book {mamId}") with { MamId = mamId };
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = mamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Torrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public static async Task<SelectedTorrentEntity> SeedSelectionAsync(
        FetchshelfDbContext context, long mamId, DateTimeOffset? startedAt, DateTimeOffset? removedAt = null)
    {
        var metadata = BuildMetadata(title: $"Book {mamId}") with { MamId = mamId };
        var entity = new SelectedTorrentEntity
        {
            MamId = mamId,
            DownloadLink = $"dl{mamId}",
            CostDecision = "Ratio",
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            StartedAt = startedAt,
            RemovedAt = removedAt,
        };
        context.SelectedTorrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }
}

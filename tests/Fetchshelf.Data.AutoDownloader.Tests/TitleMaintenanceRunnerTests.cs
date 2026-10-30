using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class TitleMaintenanceRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-title-maintenance-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task RunAsync_RecomputesTorrentTitleKeyButLeavesOtherTablesUntouched()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = "stale-key",
            Title = "The Way of Kings",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(new TorrentMetadata
            {
                MamId = 1,
                VipStatus = VipStatus.NotVip,
                MediaType = MediaType.Audiobook,
                LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
                Language = Language.English,
                Title = "The Way of Kings",
                MetadataSource = MetadataSource.Mam,
                UploadTime = DateTimeOffset.UtcNow,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        await TitleMaintenanceRunner.RunAsync(context);

        var torrent = Assert.Single(context.Torrents);
        Assert.Equal(TitleNormalizer.Normalize("The Way of Kings"), torrent.NormalizedTitleKey);
        Assert.NotEqual("stale-key", torrent.NormalizedTitleKey);
    }
}

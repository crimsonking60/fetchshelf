using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>The data upgrades <see cref="DatabaseMigrator"/> runs once, tracked by SQLite's user_version.</summary>
public sealed class DataUpgradeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-data-upgrade-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private FetchshelfDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static TorrentMetadata Metadata(string title) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Ebook,
        LegacyCategory = LegacyCategory.EbookGeneralFiction,
        Language = Language.Russian,
        Title = title,
        Authors = ["Лев Толстой"],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    /// <summary>A database as the previous version left it: keys made by the old rule, and data version 0.</summary>
    private void SeedOldDatabase()
    {
        using var context = CreateContext();
        DatabaseMigrator.Migrate(context);
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            Title = "Война и мир",
            NormalizedTitleKey = "",
            MetadataJson = TorrentMetadataSerializer.Serialize(Metadata("Война и мир")),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash2",
            MamId = 2,
            Title = "The Hobbit",
            NormalizedTitleKey = "hobbit",
            MetadataJson = TorrentMetadataSerializer.Serialize(Metadata("The Hobbit")),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 3,
            DownloadLink = "dl3",
            CostDecision = "Ratio",
            MetadataJson = TorrentMetadataSerializer.Serialize(Metadata("Анна Каренина")),
            NormalizedTitleKey = "",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        context.Database.ExecuteSqlRaw("PRAGMA user_version = 0;");
    }

    [Fact]
    public void Migrate_DatabaseAtDataVersion0_RecomputesTitleKeysAndMovesToVersion1()
    {
        SeedOldDatabase();
        var messages = new List<string>();

        using var context = CreateContext();
        DatabaseMigrator.Migrate(context, log: messages.Add);

        Assert.Equal("война и мир", context.Torrents.Single(t => t.Id == "hash1").NormalizedTitleKey);
        Assert.Equal("hobbit", context.Torrents.Single(t => t.Id == "hash2").NormalizedTitleKey);
        Assert.Equal("анна каренина", context.SelectedTorrents.Single().NormalizedTitleKey);
        Assert.Equal(DatabaseMigrator.CurrentDataVersion, DatabaseMigrator.ReadDataVersion(context));
        Assert.Equal("Recomputed the title keys of 1 torrents and 1 selections for the new title rule", messages[0]);
    }

    /// <summary>A database as version 1 left it: torrents without author, series, language or media type rows and columns.</summary>
    private void SeedVersion1Database()
    {
        SeedOldDatabase();
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw("DELETE FROM TorrentPeople; DELETE FROM TorrentSeries; UPDATE Torrents SET Language = NULL, MediaType = NULL;");
        context.Database.ExecuteSqlRaw("PRAGMA user_version = 1;");
    }

    [Fact]
    public void Migrate_DatabaseAtDataVersion1_FillsInAuthorsSeriesLanguageAndMediaType()
    {
        SeedVersion1Database();
        var messages = new List<string>();

        using var context = CreateContext();
        DatabaseMigrator.Migrate(context, log: messages.Add);

        var people = context.TorrentPeople.OrderBy(p => p.TorrentId).ToList();
        Assert.Equal(["hash1", "hash2"], people.Select(p => p.TorrentId));
        Assert.All(people, p => Assert.Equal("Лев Толстой", p.Name));
        Assert.All(context.Torrents.ToList(), t => Assert.Equal(("Russian", "Ebook"), (t.Language, t.MediaType)));
        Assert.Equal(DatabaseMigrator.CurrentDataVersion, DatabaseMigrator.ReadDataVersion(context));
        Assert.StartsWith("Filled in the authors, narrators and series of 2 torrents in ", Assert.Single(messages));
    }

    [Fact]
    public void Migrate_AuthorsAlreadyFilledIn_RunAgain_MakesNoSecondRows()
    {
        SeedVersion1Database();
        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context);
            context.Database.ExecuteSqlRaw("PRAGMA user_version = 1;");
        }

        using var reopened = CreateContext();
        DatabaseMigrator.Migrate(reopened);

        Assert.Equal(2, reopened.TorrentPeople.Count());
    }

    [Fact]
    public void Migrate_DatabaseAtTheCurrentDataVersion_LeavesTheKeysAlone()
    {
        SeedOldDatabase();
        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context);
            context.Database.ExecuteSqlRaw("UPDATE Torrents SET NormalizedTitleKey = 'set by hand' WHERE Id = 'hash1';");
        }

        var messages = new List<string>();
        using var reopened = CreateContext();
        DatabaseMigrator.Migrate(reopened, log: messages.Add);

        Assert.Equal("set by hand", reopened.Torrents.Single(t => t.Id == "hash1").NormalizedTitleKey);
        Assert.Empty(messages);
    }

    [Fact]
    public void Migrate_NewDatabase_StartsAtTheCurrentDataVersionWithoutAMessage()
    {
        var messages = new List<string>();
        using var context = CreateContext();

        DatabaseMigrator.Migrate(context, log: messages.Add);

        Assert.Equal(DatabaseMigrator.CurrentDataVersion, DatabaseMigrator.ReadDataVersion(context));
        Assert.Empty(messages);
    }
}

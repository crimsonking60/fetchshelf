using System.Reflection;
using Fetchshelf.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fetchshelf.Data.Tests;

public sealed class DataPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-data-tests-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private FetchshelfDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FetchshelfDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new FetchshelfDbContext(options);
    }

    [Fact]
    public void Migrate_FreshSqliteFile_CreatesEveryExpectedTable()
    {
        using var context = CreateContext();

        DatabaseMigrator.Migrate(context);

        var tableNames = context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table'")
            .ToList();

        string[] expectedTables =
        [
            "Torrents", "SelectedTorrents", "DuplicateTorrents", "ErroredTorrents",
            "Events", "Lists", "ListItems", "JobRuns",
        ];
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tableNames);
        }
    }

    [Fact]
    public void Migrate_WithWriteAheadLog_SwitchesTheDatabaseToIt()
    {
        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context, writeAheadLog: true);
        }

        Assert.Equal("wal", JournalMode());
    }

    [Fact]
    public void Migrate_WithoutWriteAheadLog_SwitchesAnEarlierWalDatabaseBack()
    {
        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context, writeAheadLog: true);
        }

        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context, writeAheadLog: false);
        }

        Assert.Equal("delete", JournalMode());
        Assert.False(File.Exists($"{_dbPath}-wal"));
    }

    /// <summary>The journal mode a freshly opened connection reports, i.e. the one stored in the database file.</summary>
    private string JournalMode()
    {
        SqliteConnection.ClearAllPools();
        using var context = CreateContext();
        return context.Database.SqlQueryRaw<string>("SELECT journal_mode AS Value FROM pragma_journal_mode").Single();
    }

    [Fact]
    public void Migrate_DuplicateErrorRowsFromBeforeTheUniqueIndex_KeepsOnlyTheNewestPerStageAndKey()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261023143408_ExtendListEntities");
        foreach (var (key, text) in new[] { ("hash1", "first"), ("hash1", "second"), ("hash2", "other") })
        {
            context.Database.ExecuteSql(
                $"INSERT INTO ErroredTorrents (Stage, Key, ErrorText, OccurredAt) VALUES (1, {key}, {text}, '2026-10-27 10:00:00+00:00')");
        }

        DatabaseMigrator.Migrate(context);

        var remaining = context.ErroredTorrents.OrderBy(e => e.Key).Select(e => e.ErrorText).ToList();
        Assert.Equal(["second", "other"], remaining);
    }

    [Fact]
    public void Migrate_ListFromTheInitialSchema_KeepsTheListWithEmptyNewColumns()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261022060358_Initial");
        context.Database.ExecuteSql(
            $"INSERT INTO Lists (Id, Name, ListType, CreatedAt) VALUES ('list1', 'To read', 'Goodreads', '2026-10-22 10:00:00+00:00')");
        InsertTorrent(context, "hash1", "Kept Torrent");

        context.GetService<IMigrator>().Migrate("20261023143408_ExtendListEntities");

        var lists = context.Database
            .SqlQueryRaw<string>("SELECT Name || '|' || COALESCE(BuildDate, 'null') || '|' || COALESCE(UpdatedAt, 'null') AS Value FROM Lists")
            .ToList();
        Assert.Equal(["To read|null|null"], lists);
        Assert.Equal(0, context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ListItems").Single());
        Assert.Equal("Kept Torrent", context.Database.SqlQueryRaw<string>("SELECT Title AS Value FROM Torrents").Single());
    }

    [Fact]
    public void Migrate_GoodreadsListsFromBeforeTheirRemoval_DropsTheListsAndKeepsEverythingElse()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261027192047_OneErrorRowPerTorrent");
        context.Database.ExecuteSql(
            $"INSERT INTO Lists (Id, Name, ListType, CreatedAt) VALUES ('list1', 'To read', 'Goodreads', '2026-10-28 10:00:00+00:00')");
        context.Database.ExecuteSql(
            $"""
            INSERT INTO ListItems (ListId, Guid, BookTitle, AuthorsJson, CoverUrl, AllowAudio, AllowEbook, CreatedAt)
            VALUES ('list1', 'guid1', 'Some Book', '["Some Author"]', 'https://example.test/cover.jpg', 1, 1, '2026-10-28 10:00:00+00:00')
            """);
        InsertTorrent(context, "hash1", "Kept Torrent");
        var linked = (int)EventKind.Linked;
        context.Database.ExecuteSql(
            $"INSERT INTO Events (OccurredAt, TorrentId, Kind, LibraryPath) VALUES ('2026-10-28 10:00:00+00:00', 'hash1', {linked}, '/library/Author/Title')");
        context.Database.ExecuteSql(
            $"INSERT INTO ErroredTorrents (Stage, Key, ErrorText, OccurredAt) VALUES (1, 'hash2', 'boom', '2026-10-28 10:00:00+00:00')");

        context.GetService<IMigrator>().Migrate("20261030222248_RemoveGoodreadsLists");

        var tableNames = context.Database.SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToList();
        Assert.DoesNotContain("Lists", tableNames);
        Assert.DoesNotContain("ListItems", tableNames);
        // Read with SQL: the current model has columns this older schema doesn't have yet.
        Assert.Equal("Kept Torrent", context.Database.SqlQueryRaw<string>("SELECT Title AS Value FROM Torrents").Single());
        Assert.Equal("/library/Author/Title", context.Database.SqlQueryRaw<string>("SELECT LibraryPath AS Value FROM Events").Single());
        Assert.Equal("boom", context.Database.SqlQueryRaw<string>("SELECT ErrorText AS Value FROM ErroredTorrents").Single());
    }

    [Fact]
    public void Migrate_UnusedStorageFromBeforeItsRemoval_DropsItAndKeepsEverythingElse()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261030222248_RemoveGoodreadsLists");
        context.Database.ExecuteSql($"INSERT INTO ConfigStore (Key, Value) VALUES ('mam_cookie', 'cookie-value')");
        InsertTorrent(context, "hash1", "Kept Torrent");
        context.Database.ExecuteSql(
            $$"""
            INSERT INTO SelectedTorrents (MamId, DownloadLink, InfoHash, CostDecision, UnsatBuffer, WedgeBuffer, MetadataJson, NormalizedTitleKey, CreatedAt)
            VALUES (2, 'https://example.test/download/2', 'hash2', 'Ratio', 0, 0, '{}', 'kept selected', '2026-10-31 10:00:00+00:00')
            """);

        DatabaseMigrator.Migrate(context);

        var tableNames = context.Database.SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToList();
        Assert.DoesNotContain("ConfigStore", tableNames);
        Assert.Equal("Kept Torrent", context.Torrents.Single().Title);
        Assert.Equal("kept selected", context.SelectedTorrents.Single().NormalizedTitleKey);
    }

    [Fact]
    public void Migrate_RemovedFromMamClientStatus_MovesToItsOwnColumn()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261104175626_AddHardcoverLists");
        InsertTorrent(context, "hash1", "Removed Torrent");
        InsertTorrent(context, "hash2", "Seeding Torrent");
        context.Database.ExecuteSql($"UPDATE Torrents SET ClientStatus = 'removed from MAM' WHERE Id = 'hash1'");
        context.Database.ExecuteSql($"UPDATE Torrents SET ClientStatus = 'uploading' WHERE Id = 'hash2'");

        DatabaseMigrator.Migrate(context);

        var removed = context.Torrents.Single(t => t.Id == "hash1");
        Assert.Null(removed.ClientStatus);
        Assert.NotNull(removed.RemovedFromMamAt);
        Assert.True(DateTimeOffset.UtcNow - removed.RemovedFromMamAt < TimeSpan.FromMinutes(5));
        Assert.Null(removed.MamCheckedAt);
        var seeding = context.Torrents.Single(t => t.Id == "hash2");
        Assert.Equal("uploading", seeding.ClientStatus);
        Assert.Null(seeding.RemovedFromMamAt);
    }

    [Fact]
    public void Migrate_DatabaseFromBeforeHardcoverLists_AddsEmptyListTablesAndKeepsEverythingElse()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261103113254_DropStoredSafetyBuffers");
        InsertTorrent(context, "hash1", "Kept Torrent");

        DatabaseMigrator.Migrate(context);

        Assert.Empty(context.Lists);
        Assert.Empty(context.ListItems);
        Assert.Equal("Kept Torrent", context.Torrents.Single().Title);
        context.Lists.Add(new ListEntity { Id = "hardcover:status:want_to_read", Name = "Want to Read", ListType = "Hardcover", CreatedAt = DateTimeOffset.UtcNow });
        context.ListItems.Add(new ListItemEntity
        {
            ListId = "hardcover:status:want_to_read",
            ExternalId = "312460",
            BookTitle = "Dune",
            AuthorsJson = "[\"Frank Herbert\"]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        Assert.Equal("Dune", context.ListItems.AsNoTracking().Single(i => i.ExternalId == "312460").BookTitle);
    }

    [Fact]
    public void Migrate_DatabaseFrom2026_11_7_AddsAnEmptyJobRunsTableAndKeepsEverythingElse()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate("20261110202853_AddRemovedFromMam");
        InsertTorrent(context, "hash1", "Kept Torrent");

        DatabaseMigrator.Migrate(context);

        Assert.Empty(context.JobRuns);
        Assert.Equal("Kept Torrent", context.Torrents.Single().Title);
        context.JobRuns.Add(new JobRunEntity
        {
            JobName = "Link",
            StartedAt = DateTimeOffset.UtcNow,
            Result = Fetchshelf.Core.Jobs.JobRunResult.Running,
            Trigger = Fetchshelf.Core.Jobs.JobTrigger.Startup,
        });
        context.SaveChanges();
        Assert.Equal("Link", context.JobRuns.AsNoTracking().Single().JobName);
    }

    private static void InsertTorrent(FetchshelfDbContext context, string id, string title) =>
        context.Database.ExecuteSql(
            $"INSERT INTO Torrents (Id, MamId, NormalizedTitleKey, Title, MetadataJson, CreatedAt) VALUES ({id}, 1, {title.ToLowerInvariant()}, {title}, '{{}}', '2026-10-22 10:00:00+00:00')");

    [Fact]
    public void Restart_OneRecordOfEachCoreType_AllRetrievableUnchangedAfterReopen()
    {
        var torrentId = "abc123hash";
        var now = DateTimeOffset.UtcNow;

        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context);

            context.Torrents.Add(new TorrentEntity
            {
                Id = torrentId,
                MamId = 1,
                NormalizedTitleKey = "some title",
                Title = "Some Title",
                MetadataJson = "{}",
                CreatedAt = now,
            });
            context.SelectedTorrents.Add(new SelectedTorrentEntity
            {
                MamId = 2,
                DownloadLink = "https://example.test/download/2",
                CostDecision = "Ratio",
                MetadataJson = "{}",
                NormalizedTitleKey = "another title",
                CreatedAt = now,
            });
            context.DuplicateTorrents.Add(new DuplicateTorrentEntity
            {
                MamId = 3,
                MetadataJson = "{}",
                DuplicateOfTorrentId = torrentId,
                CreatedAt = now,
            });
            context.ErroredTorrents.Add(new ErroredTorrentEntity
            {
                Stage = ErrorStage.Linker,
                Key = torrentId,
                ErrorText = "boom",
                OccurredAt = now,
            });
            context.Events.Add(new EventEntity
            {
                OccurredAt = now,
                TorrentId = torrentId,
                Kind = EventKind.Linked,
                LinkerName = "main-library",
                LibraryPath = "/library/Author/Title",
            });

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Some Title", context.Torrents.Single(t => t.Id == torrentId).Title);
            Assert.Equal("Ratio", context.SelectedTorrents.Single(s => s.MamId == 2).CostDecision);
            Assert.Equal(torrentId, context.DuplicateTorrents.Single(d => d.MamId == 3).DuplicateOfTorrentId);
            Assert.Equal("boom", context.ErroredTorrents.Single(e => e.Key == torrentId).ErrorText);
            Assert.Equal("/library/Author/Title", context.Events.Single(e => e.TorrentId == torrentId).LibraryPath);
        }
    }

    [Fact]
    public void PublicMethods_EventStore_IncludeNoUpdateOperation()
    {
        var publicMethodNames = typeof(EventStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(publicMethodNames, name => name.Contains("Update", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethodNames, name => name.Contains("Modify", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("AppendAsync", publicMethodNames);
    }

    [Fact]
    public async Task EventStore_AppendedEvent_IsRetrievableUnchanged()
    {
        using var context = CreateContext();
        DatabaseMigrator.Migrate(context);
        var store = new EventStore(context);

        await store.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.RemovedFromMam,
            MamId = 7,
        });

        var events = await context.Events.AsNoTracking().ToListAsync();
        Assert.Single(events);
        Assert.Equal(EventKind.RemovedFromMam, events[0].Kind);
        Assert.Equal(7, events[0].MamId);
    }
}

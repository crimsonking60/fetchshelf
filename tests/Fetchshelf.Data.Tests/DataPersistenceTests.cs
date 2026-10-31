using System.Reflection;
using Fetchshelf.Data.Entities;
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
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
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
            "Events", "ConfigStore",
        ];
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tableNames);
        }

        // The Goodreads list tables are dropped again by a later migration.
        Assert.DoesNotContain("Lists", tableNames);
        Assert.DoesNotContain("ListItems", tableNames);
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
            context.ConfigStore.Add(new ConfigStoreEntry { Key = "mam_cookie", Value = "cookie-value" });

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Some Title", context.Torrents.Single(t => t.Id == torrentId).Title);
            Assert.Equal("Ratio", context.SelectedTorrents.Single(s => s.MamId == 2).CostDecision);
            Assert.Equal(torrentId, context.DuplicateTorrents.Single(d => d.MamId == 3).DuplicateOfTorrentId);
            Assert.Equal("boom", context.ErroredTorrents.Single(e => e.Key == torrentId).ErrorText);
            Assert.Equal("/library/Author/Title", context.Events.Single(e => e.TorrentId == torrentId).LibraryPath);
            Assert.Equal("cookie-value", context.ConfigStore.Single(c => c.Key == "mam_cookie").Value);
        }
    }

    [Fact]
    public async Task ConfigStoreRepository_WriteThenReopen_ValueRoundTrips()
    {
        using (var context = CreateContext())
        {
            DatabaseMigrator.Migrate(context);
            var repository = new ConfigStoreRepository(context);
            await repository.SetAsync("mam_cookie", "first-value");
        }

        using (var context = CreateContext())
        {
            var repository = new ConfigStoreRepository(context);
            var value = await repository.GetAsync("mam_cookie");

            Assert.Equal("first-value", value);
        }
    }

    [Fact]
    public async Task ConfigStoreRepository_SetAsyncTwice_OverwritesExistingValue()
    {
        using var context = CreateContext();
        DatabaseMigrator.Migrate(context);
        var repository = new ConfigStoreRepository(context);

        await repository.SetAsync("mam_cookie", "first-value");
        await repository.SetAsync("mam_cookie", "second-value");

        Assert.Equal("second-value", await repository.GetAsync("mam_cookie"));
    }

    [Fact]
    public void EventStore_ExposesNoUpdateOperation()
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

        var events = await store.GetAllAsync();
        Assert.Single(events);
        Assert.Equal(EventKind.RemovedFromMam, events[0].Kind);
        Assert.Equal(7, events[0].MamId);
    }
}

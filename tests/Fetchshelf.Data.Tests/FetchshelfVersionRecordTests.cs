using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fetchshelf.Data.Tests;

/// <summary>The Fetchshelf version on record in the database, which names backups and warns an older version.</summary>
public sealed class FetchshelfVersionRecordTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-version-record-tests-").FullName;

    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private FetchshelfDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FetchshelfDbContext>().UseFetchshelfSqlite(Path.Combine(_tempDir, "data.db")).Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    private FetchshelfDbContext Migrated()
    {
        var context = CreateContext();
        DatabaseMigrator.Migrate(context);
        return context;
    }

    [Fact]
    public void Read_NewDatabase_IsNull()
    {
        using var context = CreateContext();

        Assert.Null(FetchshelfVersionRecord.Read(context));
    }

    [Fact]
    public void Read_DatabaseFromBeforeTheRecord_IsNull()
    {
        using var context = Migrated();
        var migrations = context.Database.GetMigrations().ToList();
        context.GetService<IMigrator>().Migrate(migrations[migrations.FindIndex(m => m.EndsWith("_AddDatabaseInfo", StringComparison.Ordinal)) - 1]);

        Assert.Null(FetchshelfVersionRecord.Read(context));
    }

    [Fact]
    public void RecordStartup_RecordsTheVersion_AndReplacesItOnTheNextStart()
    {
        using var context = Migrated();

        Assert.Null(FetchshelfVersionRecord.RecordStartup(context, "2026.11.11", Now));
        Assert.Equal("2026.11.11", FetchshelfVersionRecord.Read(context));

        Assert.Null(FetchshelfVersionRecord.RecordStartup(context, "2026.12.1", Now.AddDays(20)));
        Assert.Equal("2026.12.1", FetchshelfVersionRecord.Read(context));
        Assert.Equal(Now.AddDays(20), context.DatabaseInfo.AsNoTracking().Single().UpdatedAt);
    }

    [Theory]
    [InlineData("2026.11.12", "2026.11.11")]
    [InlineData("2026.12.1", "2026.11.11")]
    [InlineData("2027.1.1", "2026.12.9")]
    [InlineData("2026.11.10", "2026.11.9")]
    public void RecordStartup_OlderRelease_KeepsTheNewerOnRecord_AndReturnsIt(string recorded, string current)
    {
        using var context = Migrated();
        FetchshelfVersionRecord.RecordStartup(context, recorded, Now);

        Assert.Equal(recorded, FetchshelfVersionRecord.RecordStartup(context, current, Now.AddDays(1)));
        Assert.Equal(recorded, FetchshelfVersionRecord.Read(context));
    }

    [Theory]
    [InlineData("2026.11.11", "dev")]
    [InlineData("2026.11.11", "edge-abc123")]
    [InlineData("edge-abc123", "2026.11.11")]
    [InlineData("dev", "2026.11.11")]
    public void RecordStartup_VersionsThatCantBeCompared_RecordTheCurrentOne(string recorded, string current)
    {
        using var context = Migrated();
        FetchshelfVersionRecord.RecordStartup(context, recorded, Now);

        Assert.Null(FetchshelfVersionRecord.RecordStartup(context, current, Now.AddDays(1)));
        Assert.Equal(current, FetchshelfVersionRecord.Read(context));
    }
}

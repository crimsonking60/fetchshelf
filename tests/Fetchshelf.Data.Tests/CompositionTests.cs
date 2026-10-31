using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Confirms config loading and the persistent store can be wired together in one process, sharing a
/// base directory without colliding (config and database live alongside each other).
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly string _baseDir = Directory.CreateTempSubdirectory("fetchshelf-composition-tests-").FullName;

    public void Dispose() => Directory.Delete(_baseDir, recursive: true);

    [Fact]
    public async Task LoadingConfigAndOpeningDatabase_InSameBaseDirectory_BothSucceedWithoutConflict()
    {
        var configPath = Path.Combine(_baseDir, "config.toml");
        var dbPath = Path.Combine(_baseDir, "data.db");
        File.WriteAllText(configPath, "mam_id = \"abc123\"\n");

        var settings = ConfigLoader.Load(configPath);

        var options = new DbContextOptionsBuilder<FetchshelfDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using var dbContext = new FetchshelfDbContext(options);
        DatabaseMigrator.Migrate(dbContext);

        await new ConfigStoreRepository(dbContext).SetAsync("mam_cookie", "rotated-cookie-value");

        Assert.True(settings.BackgroundJobsEnabled);
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(dbPath));
        Assert.Equal("rotated-cookie-value", await new ConfigStoreRepository(dbContext).GetAsync("mam_cookie"));
    }
}

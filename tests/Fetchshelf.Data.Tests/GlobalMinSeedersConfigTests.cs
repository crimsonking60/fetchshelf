using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The top-level `min_seeders`, the default of every block that picks torrents to download.</summary>
public sealed class GlobalMinSeedersConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-min-seeders-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    [Fact]
    public void Load_WithoutTheSetting_EveryBlockWithoutItsOwnNeedsOneSeeder()
    {
        var settings = Load("""
            [[autograb]]
            type = "new"

            [follow]

            [[follow.grab]]

            [upgrade]
            """);

        Assert.Equal(1, settings.MinSeeders);
        Assert.Equal(1, Assert.Single(settings.Autograb).MinSeeders);
        Assert.Equal(1, Assert.Single(settings.Follow!.Grab).MinSeeders);
        // The free block [upgrade] gets when it has none.
        Assert.Equal(1, Assert.Single(settings.Upgrade!.Grab).MinSeeders);
        Assert.Equal(1, SharedFilterConfigMapper.ToTorrentFilter(settings.Autograb[0]).MinSeeders);
    }

    [Fact]
    public void Load_TopLevelMinSeeders_ReachesBlocksWithoutTheirOwn()
    {
        var settings = Load("""
            min_seeders = 3

            [[autograb]]
            name = "inherits"
            type = "new"

            [[autograb]]
            name = "none"
            type = "new"
            min_seeders = 0

            [[autograb]]
            name = "own"
            type = "new"
            min_seeders = 10

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "want_to_read"

            [[hardcover_list.grab]]
            cost = "free"

            [upgrade]

            [[upgrade.grab]]
            cost = "wedge"
            """);

        Assert.Equal([3L, 0L, 10L], settings.Autograb.Select(a => a.MinSeeders!.Value));
        Assert.Equal(3, Assert.Single(Assert.Single(settings.HardcoverList).Grab).MinSeeders);
        Assert.Equal(3, Assert.Single(settings.Upgrade!.Grab).MinSeeders);
    }

    [Fact]
    public void Load_BlockWithAMaxSeedersBelowTheTopLevelMinimum_GetsNoMinimum()
    {
        var autograb = Assert.Single(Load("""
            min_seeders = 5

            [[autograb]]
            type = "new"
            max_seeders = 2
            """).Autograb);

        Assert.Null(autograb.MinSeeders);
        Assert.Equal(2, autograb.MaxSeeders);
    }

    [Fact]
    public void Load_TagRules_DoNotInheritTheTopLevelMinSeeders()
    {
        var rule = Assert.Single(Load("""
            min_seeders = 2

            [[tag]]
            category = "ebooks"
            """).Tag);

        Assert.Null(rule.MinSeeders);
        Assert.Null(SharedFilterConfigMapper.ToTorrentFilter(rule).MinSeeders);
    }

    [Fact]
    public void Load_NegativeTopLevelMinSeeders_Throws()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load("min_seeders = -1\n"));

        Assert.Contains("'min_seeders'", exception.Message);
    }

    [Fact]
    public void Load_TopLevelMinSeedersFromEnvironmentVariable_ReachesAutograbs()
    {
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_min_seeders", "4");
        try
        {
            Assert.Equal(4, Assert.Single(Load("[[autograb]]\ntype = \"new\"\n").Autograb).MinSeeders);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_min_seeders", null);
        }
    }
}

using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[upgrade]` section.</summary>
public sealed class UpgradeConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-upgrade-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private const string Library = """

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "/library/audiobooks"
        """;

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_FullSection_ReadsEverySetting()
    {
        var upgrade = Load("""
            mam_id = ""
            languages = ["English"]

            [upgrade]
            search_interval = 360
            searches_per_run = 5
            search_again_after = 60
            libraries = ["Audiobooks"]
            dry_run = true

            [[upgrade.grab]]
            cost = "wedge"
            """ + Library).Upgrade!;

        Assert.Equal((360, 5, 60, true), (upgrade.SearchInterval, upgrade.SearchesPerRun, upgrade.SearchAgainAfter, upgrade.DryRun));
        Assert.Equal(["Audiobooks"], upgrade.Libraries);
        var grab = Assert.Single(upgrade.Grab);
        Assert.Equal(ConfiguredCost.Wedge, grab.Cost);
        // A better copy is searched in the book's own language, so the top-level languages don't reach the block.
        Assert.Null(grab.Languages);
    }

    [Fact]
    public void Load_Defaults_IncludeOneFreeGrabBlock()
    {
        var upgrade = Load("mam_id = \"\"\n\n[upgrade]\n").Upgrade!;

        Assert.Equal((null, 20, 30, false), (upgrade.SearchInterval, upgrade.SearchesPerRun, upgrade.SearchAgainAfter, upgrade.DryRun));
        Assert.Empty(upgrade.Libraries);
        Assert.Equal(ConfiguredCost.Free, Assert.Single(upgrade.Grab).Cost);
    }

    [Fact]
    public void Load_NoSection_NoUpgradeJob() =>
        Assert.Null(Load("mam_id = \"\"\n").Upgrade);

    [Fact]
    public void Load_AnUnknownLibrary_IsRejected() =>
        Assert.Contains("[upgrade] 'libraries' names 'Ebooks', but no [[library]] has that name",
            LoadFails("mam_id = \"\"\n\n[upgrade]\nlibraries = [\"Ebooks\"]\n" + Library).Message);

    [Theory]
    [InlineData("searches_per_run = 0", "'searches_per_run' in the [upgrade] section must be 1 or more")]
    [InlineData("search_again_after = 0", "'search_again_after' in the [upgrade] section must be between 1 and 365 days")]
    [InlineData("search_interval = 50000", "'search_interval' in the [upgrade] section must be between 0")]
    public void Load_ValueOutOfRange_IsRejected(string line, string message) =>
        Assert.Contains(message, LoadFails($"mam_id = \"\"\n\n[upgrade]\n{line}\n").Message);

    [Fact]
    public void Load_MetadataOnlyCost_IsRejected() =>
        Assert.Contains("metadata-only 'cost'", LoadFails("mam_id = \"\"\n\n[upgrade]\n\n[[upgrade.grab]]\ncost = \"metadata_only\"\n").Message);

    [Fact]
    public void Load_ABlockNamedUpgrade_IsRejected() =>
        Assert.Contains("'Upgrade' is the name of a built-in job",
            LoadFails("mam_id = \"\"\n\n[[autograb]]\nname = \"Upgrade\"\ntype = \"new\"\n").Message);
}

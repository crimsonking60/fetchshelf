using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[bonus]` section.</summary>
public sealed class BonusConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-bonus-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_NoSection_BuysNothing()
    {
        Assert.Null(Load("").Bonus);
    }

    [Fact]
    public void Load_EmptySection_HasTheDefaults()
    {
        var bonus = Load("[bonus]\n").Bonus!;

        Assert.Equal((120, 0L, 0, false, 10, false), (bonus.SearchInterval, bonus.KeepPoints, bonus.WedgeEvery, bonus.Vip, bonus.MaxPurchasesPerRun, bonus.DryRun));
        Assert.Equal([100, 50, 20, 5, 1], bonus.UploadAmounts);
    }

    [Fact]
    public void Load_FullSection_ReadsEverySetting()
    {
        var bonus = Load("""
            [bonus]
            search_interval = 240
            keep_points = 55000
            upload_amounts = [100, 50]
            wedge_every = 24
            vip = true
            max_purchases_per_run = 3
            dry_run = true
            """).Bonus!;

        Assert.Equal((240, 55_000L, 24, true, 3, true), (bonus.SearchInterval, bonus.KeepPoints, bonus.WedgeEvery, bonus.Vip, bonus.MaxPurchasesPerRun, bonus.DryRun));
        Assert.Equal([100, 50], bonus.UploadAmounts);
    }

    [Fact]
    public void Load_NoUploadAmounts_BuysNoUploadCredit()
    {
        Assert.Empty(Load("[bonus]\nupload_amounts = []\n").Bonus!.UploadAmounts);
    }

    [Theory]
    [InlineData("search_interval = 30", "'search_interval' in the [bonus] section must be 0 (run only from the Jobs page) or at least 60 minutes; got 30.")]
    [InlineData("search_interval = 50000", "'search_interval' in the [bonus] section must be between 0")]
    [InlineData("keep_points = -1", "'keep_points' in the [bonus] section must be 0 or more; got -1.")]
    [InlineData("upload_amounts = [50, 0]", "'upload_amounts' in the [bonus] section must hold whole numbers of GB from 1 to 100; got 0.")]
    [InlineData("upload_amounts = [500]", "got 500.")]
    [InlineData("wedge_every = 9000", "'wedge_every' in the [bonus] section must be between 0 (buy no wedges) and 8760 hours (a year); got 9000.")]
    [InlineData("max_purchases_per_run = 0", "'max_purchases_per_run' in the [bonus] section must be between 1 and 100; got 0.")]
    public void Load_ValueOutOfRange_IsRefused(string setting, string message)
    {
        var error = LoadFails($"[bonus]\n{setting}\n");

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SearchIntervalZero_RunsOnlyFromTheJobsPage()
    {
        Assert.Equal(0, Load("[bonus]\nsearch_interval = 0\n").Bonus!.SearchInterval);
    }

    [Fact]
    public void Load_UnknownSetting_IsRefused()
    {
        LoadFails("[bonus]\nbuy_upload_above = 50000\n");
    }

    [Fact]
    public void Load_BlockNamedBonus_IsRefused()
    {
        var error = LoadFails("""
            [[autograb]]
            name = "Bonus"
            type = "bookmarks"
            cost = "free"
            """);

        Assert.Contains("'Bonus' is the name of a built-in job", error.Message, StringComparison.Ordinal);
    }
}

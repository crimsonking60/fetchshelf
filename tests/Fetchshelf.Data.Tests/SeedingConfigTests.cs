using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class SeedingConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-seeding-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    [Fact]
    public void Load_NoSeedingSection_LeavesTheJobOff() => Assert.Null(Load("").Seeding);

    [Fact]
    public void Load_EmptySeedingSection_HasTheDefaults()
    {
        var seeding = Load("[seeding]\n").Seeding!;

        Assert.Equal((72, 30, 60, null, true), (seeding.SeedHours, seeding.WithinDays, seeding.Interval, seeding.TagUnsatisfied, seeding.WarnBeforeRemoval));
    }

    [Fact]
    public void Load_EverySetting_IsRead()
    {
        var seeding = Load("[seeding]\nseed_hours = 48\nwithin_days = 20\ninterval = 0\ntag_unsatisfied = \"unsat\"\nwarn_before_removal = false\n").Seeding!;

        Assert.Equal((48, 20, 0, "unsat", false), (seeding.SeedHours, seeding.WithinDays, seeding.Interval, seeding.TagUnsatisfied, seeding.WarnBeforeRemoval));
    }

    [Theory]
    [InlineData("seed_hours = 0", "'seed_hours'")]
    [InlineData("within_days = 0", "'within_days'")]
    [InlineData("within_days = 400", "'within_days'")]
    [InlineData("interval = -1", "'interval'")]
    [InlineData("tag_unsatisfied = \"a,b\"", "'tag_unsatisfied'")]
    [InlineData("tag_unsatisfied = \" \"", "'tag_unsatisfied'")]
    public void Load_ValueOutOfRange_StopsStartup(string setting, string expected)
    {
        var error = Assert.Throws<ConfigurationException>(() => Load("[seeding]\n" + setting + "\n"));

        Assert.Contains(expected, error.Message);
        Assert.Contains("[seeding]", error.Message);
    }

    [Fact]
    public void Load_ABlockNamedSeeding_IsRefused() =>
        Assert.Contains("built-in job", Assert.Throws<ConfigurationException>(() => Load("[[autograb]]\nname = \"Seeding\"\ntype = \"new\"\n")).Message);
}

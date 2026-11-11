using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[follow]` section.</summary>
public sealed class FollowConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-follow-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

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
        var settings = Load("""
            mam_id = ""
            languages = ["English"]

            [follow]
            search_interval = 120
            searches_per_run = 5
            search_again_after = 48
            prefer_format = "audio"
            category = "audiobooks"
            dry_run = true

            [[follow.grab]]
            cost = "free"

            [[follow.grab]]
            cost = "wedge"
            languages = ["German"]
            """);

        var follow = Assert.IsType<FollowSettings>(settings.Follow);
        Assert.Equal((120, 5, 48), (follow.SearchInterval, follow.SearchesPerRun, follow.SearchAgainAfter));
        Assert.Equal((FilterMainType.Audio, "audiobooks", true), (follow.PreferFormat, follow.Category, follow.DryRun));
        Assert.Equal([ConfiguredCost.Free, ConfiguredCost.Wedge], follow.Grab.Select(g => g.Cost));
        // The top-level languages reach a block that sets none.
        Assert.Equal(["English"], follow.Grab[0].Languages);
        Assert.Equal(["German"], follow.Grab[1].Languages);
    }

    [Fact]
    public void Load_Defaults()
    {
        var follow = Load("""
            mam_id = ""

            [follow]

            [[follow.grab]]
            """).Follow!;

        Assert.Equal((null, 10, 24), (follow.SearchInterval, follow.SearchesPerRun, follow.SearchAgainAfter));
        Assert.Equal((null, null, false), (follow.PreferFormat, follow.Category, follow.DryRun));
        Assert.Equal(ConfiguredCost.Free, Assert.Single(follow.Grab).Cost);
    }

    [Fact]
    public void Load_NoSection_NoFollowing() =>
        Assert.Null(Load("mam_id = \"\"\n").Follow);

    [Fact]
    public void Load_NoGrabBlock_IsRejected() =>
        Assert.Contains("at least one [[follow.grab]] block", LoadFails("""
            mam_id = ""

            [follow]
            searches_per_run = 5
            """).Message);

    [Theory]
    [InlineData("searches_per_run = 0", "'searches_per_run' in the [follow] section must be 1 or more")]
    [InlineData("search_again_after = 0", "'search_again_after' in the [follow] section must be between 1 and 720 hours")]
    [InlineData("search_again_after = 721", "'search_again_after' in the [follow] section must be between 1 and 720 hours")]
    [InlineData("search_interval = -1", "'search_interval' in the [follow] section must be between 0")]
    [InlineData("prefer_format = \"musicology\"", "'prefer_format' that is not 'audio' or 'ebook'")]
    public void Load_ValueOutOfRange_IsRejected(string line, string message) =>
        Assert.Contains(message, LoadFails($"""
            mam_id = ""

            [follow]
            {line}

            [[follow.grab]]
            """).Message);

    [Fact]
    public void Load_MetadataOnlyCost_IsRejected() =>
        Assert.Contains("metadata-only 'cost'", LoadFails("""
            mam_id = ""

            [follow]

            [[follow.grab]]
            cost = "metadata_only"
            """).Message);

    [Fact]
    public void Load_UnknownKey_IsRejected() =>
        LoadFails("""
            mam_id = ""

            [follow]
            searches = 5

            [[follow.grab]]
            """);

    [Fact]
    public void Load_ABlockNamedFollow_IsRejected() =>
        Assert.Contains("'Follow' is the name of a built-in job", LoadFails("""
            mam_id = ""

            [[autograb]]
            name = "Follow"
            type = "new"
            """).Message);
}

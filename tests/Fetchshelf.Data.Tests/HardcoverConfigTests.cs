using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// The `[hardcover]` and `[[hardcover_list]]` sections. The environment-variable test lives here too, so it never runs
/// alongside the other tests in this class that expect no `[hardcover]` section.
/// </summary>
public sealed class HardcoverConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-hardcover-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_FullConfig_ReadsEverySetting()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            name = "Want to Read"
            status = "want_to_read"
            prefer_format = "audio"
            search_interval = 360
            dry_run = true

            [[hardcover_list.grab]]
            cost = "free"
            languages = ["English"]

            [[hardcover_list.grab]]
            cost = "wedge"

            [[hardcover_list]]
            list = "books-to-fetch"

            [[hardcover_list.grab]]
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal("hc-token", settings.Hardcover!.Token);
        Assert.Equal(2, settings.HardcoverList.Count);
        var first = settings.HardcoverList[0];
        Assert.Equal("Want to Read", first.Name);
        Assert.Equal(HardcoverListStatus.WantToRead, first.Status);
        Assert.Null(first.List);
        Assert.Equal(FilterMainType.Audio, first.PreferFormat);
        Assert.Equal(360, first.SearchInterval);
        Assert.True(first.DryRun);
        Assert.Equal([ConfiguredCost.Free, ConfiguredCost.Wedge], first.Grab.Select(g => g.Cost));
        Assert.Equal(["English"], first.Grab[0].Languages);
        var second = settings.HardcoverList[1];
        Assert.Null(second.Status);
        Assert.Equal("books-to-fetch", second.List);
        Assert.Equal(ConfiguredCost.Free, Assert.Single(second.Grab).Cost);
    }

    [Theory]
    [InlineData("want_to_read", HardcoverListStatus.WantToRead)]
    [InlineData("currently_reading", HardcoverListStatus.CurrentlyReading)]
    [InlineData("read", HardcoverListStatus.Read)]
    [InlineData("paused", HardcoverListStatus.Paused)]
    [InlineData("did_not_finish", HardcoverListStatus.DidNotFinish)]
    public void Load_EveryStatus_IsRecognized(string value, HardcoverListStatus expected)
    {
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "{value}"

            [[hardcover_list.grab]]
            """);

        Assert.Equal(expected, ConfigLoader.Load(ConfigPath).HardcoverList[0].Status);
    }

    [Fact]
    public void Load_NoHardcoverSections_LeavesThemEmpty()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Null(settings.Hardcover);
        Assert.Empty(settings.HardcoverList);
    }

    [Fact]
    public void Load_ListWithoutHardcoverSection_Throws()
    {
        var exception = LoadFails("""
            mam_id = ""

            [[hardcover_list]]
            name = "Wishlist"
            status = "want_to_read"

            [[hardcover_list.grab]]
            """);

        Assert.Contains("'Wishlist'", exception.Message);
        Assert.Contains("[hardcover]", exception.Message);
    }

    [Fact]
    public void Load_HardcoverWithoutToken_Throws()
    {
        var exception = LoadFails("""
            mam_id = ""

            [hardcover]
            """);

        Assert.Contains("'token' or 'token_file'", exception.Message);
    }

    [Theory]
    [InlineData("status = \"want_to_read\"\nlist = \"wishlist\"")]
    [InlineData("name = \"Neither\"")]
    public void Load_StatusAndListBothOrNeither_Throws(string source)
    {
        var exception = LoadFails($"""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            {source}

            [[hardcover_list.grab]]
            """);

        Assert.Contains("one of 'status' or 'list'", exception.Message);
    }

    [Fact]
    public void Load_UnknownStatus_Throws()
    {
        LoadFails("""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "to_be_read"

            [[hardcover_list.grab]]
            """);
    }

    [Fact]
    public void Load_NoGrabBlock_Throws()
    {
        var exception = LoadFails("""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "want_to_read"
            """);

        Assert.Contains("'grab'", exception.Message);
    }

    [Fact]
    public void Load_PreferFormatOtherThanAudioOrEbook_Throws()
    {
        var exception = LoadFails("""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "want_to_read"
            prefer_format = "radio"

            [[hardcover_list.grab]]
            """);

        Assert.Contains("prefer_format", exception.Message);
    }

    [Fact]
    public void Load_MetadataOnlyCost_Throws()
    {
        var exception = LoadFails("""
            mam_id = ""

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "want_to_read"

            [[hardcover_list.grab]]
            cost = "metadata_only_add"
            """);

        Assert.Contains("metadata-only", exception.Message);
    }

    [Fact]
    public void Load_TokenFile_IsReadAndTrimmed()
    {
        var secretPath = Path.Combine(_tempDir, "hardcover_token");
        File.WriteAllText(secretPath, "from-file\n");
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""

            [hardcover]
            token_file = "{secretPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        Assert.Equal("from-file", ConfigLoader.Load(ConfigPath).Hardcover!.Token);
    }

    [Fact]
    public void Load_TokenAndTokenFile_Throws()
    {
        var secretPath = Path.Combine(_tempDir, "hardcover_token");
        File.WriteAllText(secretPath, "from-file");
        var exception = LoadFails($"""
            mam_id = ""

            [hardcover]
            token = "hc-token"
            token_file = "{secretPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        Assert.Contains("not both", exception.Message);
    }

    [Fact]
    public void Load_TokenFromEnvironment_CreatesTheSection()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_hardcover_token", "from-env");
        try
        {
            Assert.Equal("from-env", ConfigLoader.Load(ConfigPath).Hardcover!.Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_hardcover_token", null);
        }
    }
}

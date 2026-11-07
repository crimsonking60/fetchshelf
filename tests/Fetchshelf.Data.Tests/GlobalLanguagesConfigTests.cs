using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class GlobalLanguagesConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-languages-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    [Fact]
    public void Load_AutograbsWithoutTheirOwnLanguages_GetTheTopLevelOnes()
    {
        var settings = Load("""
            languages = ["English", "German"]

            [[autograb]]
            name = "inherits"
            type = "new"

            [[autograb]]
            name = "any language"
            type = "new"
            languages = []

            [[autograb]]
            name = "own"
            type = "new"
            languages = ["French"]
            """);

        Assert.Equal(["English", "German"], settings.Autograb[0].Languages);
        Assert.Equal([], settings.Autograb[1].Languages!);
        Assert.Equal(["French"], settings.Autograb[2].Languages);
        Assert.Null(SharedFilterConfigMapper.ToTorrentFilter(settings.Autograb[1]).Languages);
    }

    [Fact]
    public void Load_HardcoverGrabsWithoutTheirOwnLanguages_GetTheTopLevelOnes()
    {
        var settings = Load("""
            languages = ["English"]

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            status = "want_to_read"

            [[hardcover_list.grab]]
            cost = "free"

            [[hardcover_list.grab]]
            cost = "wedge"
            languages = []
            """);

        var grabs = Assert.Single(settings.HardcoverList).Grab;
        Assert.Equal(["English"], grabs[0].Languages);
        Assert.Equal([], grabs[1].Languages!);
    }

    [Fact]
    public void Load_TagRules_DoNotInheritTheTopLevelLanguages()
    {
        var settings = Load("""
            languages = ["English"]

            [[tag]]
            category = "ebooks"
            """);

        var rule = Assert.Single(settings.Tag);
        Assert.Null(rule.Languages);
        Assert.Null(SharedFilterConfigMapper.ToTorrentFilter(rule).Languages);
    }

    [Fact]
    public void Load_Snatchlists_DoNotInheritTheTopLevelLanguages()
    {
        var settings = Load("""
            languages = ["English"]

            [[snatchlist]]
            type = "unsat"
            """);

        Assert.Null(Assert.Single(settings.Snatchlist).Languages);
    }

    [Fact]
    public void Load_NoTopLevelLanguages_LeavesAutograbsUnrestricted()
    {
        var settings = Load("""
            [[autograb]]
            type = "new"
            """);

        Assert.Empty(settings.Languages);
        Assert.Null(SharedFilterConfigMapper.ToTorrentFilter(Assert.Single(settings.Autograb)).Languages);
    }

    [Fact]
    public void Load_TopLevelLanguagesUseTheSameNamesAsBlocks()
    {
        var settings = Load("""
            languages = ["brazilian", "ancient greek"]

            [[autograb]]
            type = "new"
            """);

        Assert.Equal(
            [Language.BrazilianPortuguese, Language.AncientGreek],
            SharedFilterConfigMapper.ToTorrentFilter(Assert.Single(settings.Autograb)).Languages);
    }

    [Fact]
    public void Load_UnknownTopLevelLanguage_Throws()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load("languages = [\"Klingon\"]\n"));

        Assert.Contains("Klingon", exception.Message);
        Assert.Contains("languages", exception.Message);
    }
}

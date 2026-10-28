using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class AutogrebConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-autograb-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MissingType_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            cost = "free"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("type", exception.Message);
    }

    [Fact]
    public void Load_MaxAuthors_IsParsedFromToml()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "new"
            search_in = ["author"]
            max_authors = 3
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(3, settings.Autograb[0].MaxAuthors);
    }

    [Fact]
    public void Load_MaxActiveDownloadsWithoutName_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "bookmarks"
            max_active_downloads = 3
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void Load_EntryWithOnlyType_UsesDefaults()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "bookmarks"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        var entry = Assert.Single(settings.Autograb);
        Assert.Equal("bookmarks", entry.Type!.Kind);
        Assert.Equal(ConfiguredCost.Free, entry.Cost);
        Assert.Equal(50, entry.EffectiveMaxPages);
    }

    [Fact]
    public void Load_NonBookmarksType_DefaultsMaxPagesToOne()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "new"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).Autograb);

        Assert.Equal(1, entry.EffectiveMaxPages);
    }

    [Fact]
    public void Load_UploaderType_ParsesId()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = { uploader = 42 }
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).Autograb);

        Assert.Equal("uploader", entry.Type!.Kind);
        Assert.Equal(42, entry.Type.UploaderId);
    }

    [Fact]
    public void Load_CostAliasAll_ResolvesToRatio()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "bookmarks"
            cost = "all"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).Autograb);

        Assert.Equal(ConfiguredCost.Ratio, entry.Cost);
    }
}

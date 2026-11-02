using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class AutograbConfigTests : IDisposable
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

    [Theory]
    [InlineData("new")]
    [InlineData("torrents")]
    public void Load_NewTypeAndItsTorrentsAlias_ParseToTheSameType(string type)
    {
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""

            [[autograb]]
            type = "{type}"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Same(AutograbType.New, settings.Autograb[0].Type);
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

    [Theory]
    [InlineData("newest_first")]
    [InlineData("oldest_first")]
    [InlineData("random")]
    [InlineData("low_seeders")]
    [InlineData("low_snatches")]
    [InlineData("Oldest_First")]
    public void Load_KnownSortBy_Loads(string sortBy)
    {
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""

            [[autograb]]
            type = "new"
            sort_by = "{sortBy}"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).Autograb);

        Assert.Equal(sortBy, entry.SortBy);
    }

    [Fact]
    public void Load_UnknownSortBy_ThrowsNamingTheValue()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "new"
            sort_by = "oldest"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("'sort_by' value 'oldest'", exception.Message);
        Assert.Contains("oldest_first", exception.Message);
    }

    [Theory]
    [InlineData("isbn")]
    [InlineData("3")]
    public void Load_UnknownSearchInField_ThrowsNamingTheField(string field)
    {
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""

            [[autograb]]
            type = "new"
            search_in = ["author", "{field}"]
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains($"'search_in' field '{field}'", exception.Message);
        Assert.Contains("file_types", exception.Message);
    }

    [Fact]
    public void Load_MisspelledType_NamesTheValueInsteadOfAGenericDeserializationError()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "freelech"
            cost = "free"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Equal("Invalid configuration: unrecognized autograb type 'freelech'.", exception.Message);
    }

    [Fact]
    public void Load_MisspelledCost_NamesTheValue()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = "freeleech"
            cost = "fre"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Equal("Invalid configuration: unrecognized cost 'fre'.", exception.Message);
    }

    [Fact]
    public void Load_UploaderIdNotANumber_SaysWhatIsWrong()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[autograb]]
            type = { uploader = "someone" }
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Equal(
            "Invalid configuration: autograb type 'uploader' needs the uploader's MAM user id, a number, not 'someone'.",
            exception.Message);
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

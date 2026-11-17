using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>A `[[library]]`'s `layout`, `oneshots_dir` and `series_json`.</summary>
public sealed class SeriesLayoutConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-series-layout-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private Library Load(string librarySettings, string topLevel = "")
    {
        File.WriteAllText(ConfigPath, $"""
            mam_id = ""
            {topLevel}

            [[library]]
            name = "Comics"
            library_dir = "/library/comics"
            category = "comics"
            {librarySettings}
            """);
        return Assert.Single(ConfigLoader.Load(ConfigPath).Library);
    }

    private ConfigurationException LoadFails(string librarySettings, string topLevel = "") =>
        Assert.Throws<ConfigurationException>(() => Load(librarySettings, topLevel));

    [Fact]
    public void Load_NoLayout_IsTheBookLayout()
    {
        var library = Load("");

        Assert.Equal(LibraryLayout.Book, library.Layout);
        Assert.False(library.IsSeriesLayout);
    }

    [Fact]
    public void Load_SeriesLayout_HasItsDefaults_AndNoPerBookMetadataFiles()
    {
        var library = Load("layout = \"series\"");

        Assert.True(library.IsSeriesLayout);
        Assert.Equal("_oneshots", library.OneshotsDir);
        Assert.True(library.SeriesJson);
        Assert.False(library.MetadataJson);
        Assert.False(library.MetadataOpf);
    }

    [Fact]
    public void Load_GlobalMetadataFiles_DontApplyToASeriesLibrary()
    {
        var library = Load("layout = \"series\"", topLevel: "metadata_json = true\nmetadata_opf = true");

        Assert.False(library.MetadataJson);
        Assert.False(library.MetadataOpf);
    }

    [Fact]
    public void Load_SeriesSettings_AreRead()
    {
        var library = Load("layout = \"series\"\noneshots_dir = \"One-Shots\"\nseries_json = false");

        Assert.Equal(("One-Shots", false), (library.OneshotsDir, library.SeriesJson));
    }

    [Theory]
    [InlineData("oneshots_dir = \"_oneshots\"")]
    [InlineData("series_json = true")]
    public void Load_SeriesSettingOnABookLibrary_Throws(string setting)
    {
        var error = LoadFails(setting);

        Assert.Contains("the [[library]] 'Comics' sets 'oneshots_dir' or 'series_json', which only a library with layout = \"series\" uses", error.Message);
    }

    [Theory]
    [InlineData("metadata_json = true")]
    [InlineData("metadata_opf = true")]
    public void Load_PerBookMetadataFileInASeriesLibrary_Throws(string setting)
    {
        var error = LoadFails($"layout = \"series\"\n{setting}");

        Assert.Contains("has layout = \"series\" and 'metadata_json' or 'metadata_opf'", error.Message);
    }

    [Fact]
    public void Load_MetadataFilesTurnedOffInASeriesLibrary_IsFine() =>
        Assert.False(Load("layout = \"series\"\nmetadata_json = false").MetadataJson);

    [Fact]
    public void Load_FolderTemplateInASeriesLibrary_Throws()
    {
        var error = LoadFails("layout = \"series\"\nfolder_template = \"{series}/{title}\"");

        Assert.Contains("has layout = \"series\" and a 'folder_template'", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("one:shots")]
    public void Load_OneshotsDirThatIsNoFolderName_Throws(string oneshots)
    {
        var error = LoadFails($"layout = \"series\"\nfolder_names = \"portable\"\noneshots_dir = \"{oneshots}\"");

        Assert.Contains("has an 'oneshots_dir' that is not a folder name", error.Message);
    }

    [Fact]
    public void Load_UnknownLayout_Throws()
    {
        var error = LoadFails("layout = \"comics\"");

        Assert.Contains("'comics' is not one of book, series", error.Message);
    }
}

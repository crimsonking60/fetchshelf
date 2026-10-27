using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class SearchSettingsConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-search-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_SearchSectionOmitted_WedgeOverIsUnset()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Null(settings.Search.WedgeOver);
    }

    [Fact]
    public void Load_SearchSection_ParsesWedgeOver()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [search]
            wedge_over = "1 GiB"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal("1 GiB", settings.Search.WedgeOver);
    }
}

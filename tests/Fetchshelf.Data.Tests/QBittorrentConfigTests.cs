using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class QBittorrentConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-qbt-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MultipleQBittorrentSections_ParsesEachIntoConnectionEntry()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [[qbittorrent]]
            url = "http://otherhost:9090"
            username = "admin2"
            password = "pass2"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(2, settings.QBittorrent.Count);
        Assert.Equal("http://localhost:8080", settings.QBittorrent[0].Url);
        Assert.Equal("admin", settings.QBittorrent[0].Username);
        Assert.Equal("adminadmin", settings.QBittorrent[0].Password);
        Assert.Equal("http://otherhost:9090", settings.QBittorrent[1].Url);
    }

    [Fact]
    public void Load_NoQBittorrentSection_DefaultsToEmptyList()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Empty(settings.QBittorrent);
    }

    [Theory]
    [InlineData("""
        [[qbittorrent]]
        username = "admin"
        password = "adminadmin"
        """, "url")]
    [InlineData("""
        [[qbittorrent]]
        url = "http://localhost:8080"
        password = "adminadmin"
        """, "username")]
    [InlineData("""
        [[qbittorrent]]
        url = "http://localhost:8080"
        username = "admin"
        """, "password")]
    public void Load_QBittorrentSectionMissingRequiredField_ThrowsNamingTheField(string tomlBody, string missingField)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + tomlBody + "\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains(missingField, exception.Message);
    }
}

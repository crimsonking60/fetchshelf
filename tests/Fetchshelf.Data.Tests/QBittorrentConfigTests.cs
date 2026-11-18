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

    private const string TwoNamedQBittorrents = """
        mam_id = ""

        [[qbittorrent]]
        name = "home"
        url = "http://localhost:8080"
        username = "admin"
        password = "adminadmin"

        [[qbittorrent]]
        name = "VPN"
        url = "http://otherhost:9090"
        username = "admin2"
        password = "pass2"

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "/library/audiobooks"

        """;

    [Fact]
    public void Load_ClientAndSavePath_ReadInEveryKindOfBlock()
    {
        File.WriteAllText(ConfigPath, TwoNamedQBittorrents + """
            [[autograb]]
            type = "new"
            client = "vpn"
            save_path = "/data/new"

            [[tag]]
            client = "home"
            save_path = 'D:\Books'

            [follow]
            client = "VPN"
            save_path = "/data/follow"

            [[follow.grab]]
            cost = "free"

            [upgrade]
            client = "home"
            save_path = "/data/upgrade"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(["home", "VPN"], settings.QBittorrent.Select(q => q.Name));
        Assert.Equal(("vpn", "/data/new"), (settings.Autograb[0].Client, settings.Autograb[0].SavePath));
        Assert.Equal(("home", @"D:\Books"), (settings.Tag[0].Client, settings.Tag[0].SavePath));
        Assert.Equal(("VPN", "/data/follow"), (settings.Follow!.Client, settings.Follow.SavePath));
        Assert.Equal(("home", "/data/upgrade"), (settings.Upgrade!.Client, settings.Upgrade.SavePath));
    }

    [Fact]
    public void Load_TwoQBittorrentsWithTheSameName_IgnoringCase_Throws()
    {
        File.WriteAllText(ConfigPath, TwoNamedQBittorrents.Replace("name = \"VPN\"", "name = \"Home\"", StringComparison.Ordinal));

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("same name", exception.Message);
        Assert.Contains("Home", exception.Message);
    }

    [Fact]
    public void Load_ClientNamingNoQBittorrent_Throws()
    {
        File.WriteAllText(ConfigPath, TwoNamedQBittorrents + """
            [[autograb]]
            name = "New books"
            type = "new"
            client = "seedbox"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("[[autograb]] 'New books'", exception.Message);
        Assert.Contains("seedbox", exception.Message);
        Assert.Contains("\"home\", \"VPN\"", exception.Message);
    }

    [Theory]
    [InlineData("downloads/books")]
    [InlineData("./books")]
    [InlineData("D:Books")]
    public void Load_RelativeSavePath_Throws(string savePath)
    {
        File.WriteAllText(ConfigPath, TwoNamedQBittorrents + $"""
            [[tag]]
            save_path = '{savePath}'
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("[[tag]]", exception.Message);
        Assert.Contains("absolute", exception.Message);
    }
}

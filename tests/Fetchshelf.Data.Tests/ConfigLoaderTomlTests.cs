using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class ConfigLoaderTomlTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-toml-config-tests-").FullName;

    private string TomlConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_ValidTomlConfigFile_ParsesNestedTablesAndArraysOfTables()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = "abc123"
            min_ratio = 1.5
            ignore_torrents = ["foo", "bar"]

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [qbittorrent.path_mapping]
            "/downloads" = "/data"

            [[qbittorrent]]
            url = "http://otherhost:9090"
            username = "admin2"
            password = "pass2"
            """);

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.Equal("abc123", settings.MamId);
        Assert.Equal(1.5, settings.MinRatio);
        Assert.Equal(["foo", "bar"], settings.IgnoreTorrents);
        Assert.Equal(2, settings.QBittorrent.Count);
        Assert.Equal("http://localhost:8080", settings.QBittorrent[0].Url);
        Assert.Equal("admin", settings.QBittorrent[0].Username);
        Assert.Equal("adminadmin", settings.QBittorrent[0].Password);
        Assert.Equal("/data", settings.QBittorrent[0].PathMapping["/downloads"]);
        Assert.Equal("http://otherhost:9090", settings.QBittorrent[1].Url);
        Assert.Equal("admin2", settings.QBittorrent[1].Username);
        Assert.Equal("pass2", settings.QBittorrent[1].Password);
    }

    [Fact]
    public void Load_AudiobookshelfPathMapping_IsRead()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = "abc123"

            [audiobookshelf]
            url = "http://abs:13378"
            token = "secret"

            [audiobookshelf.path_mapping]
            "/mnt/Data/media/abs" = "/media"
            """);

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.Equal("/media", settings.Audiobookshelf!.PathMapping["/mnt/Data/media/abs"]);
    }

    [Fact]
    public void Load_TomlArraysOfTablesInterleavedWithOtherArrays_KeepsEveryEntry()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = "abc123"

            [[tag]]
            category = "ebooks"

            [[library]]
            category = "ebooks"
            library_dir = "/lib/ebooks"

            [[tag]]
            category = "audiobooks"

            [[library]]
            category = "audiobooks"
            library_dir = "/lib/audiobooks"

            [[tag]]
            category = "comics"

            [[library]]
            category = "comics"
            library_dir = "/lib/comics"
            """);

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.Equal(["ebooks", "audiobooks", "comics"], settings.Tag.Select(t => t.Category));
        Assert.Equal(["ebooks", "audiobooks", "comics"], settings.Library.Select(l => l.Category));
    }

    [Fact]
    public void Load_TomlDuplicateScalarKey_Throws()
    {
        File.WriteAllText(TomlConfigPath, "mam_id = \"abc123\"\nmam_id = \"def\"\n");

        Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));
    }

    [Fact]
    public void Load_TomlUnknownTopLevelKey_ThrowsNamingTheKey()
    {
        File.WriteAllText(TomlConfigPath, "mam_id = \"abc123\"\nnot_a_real_setting = 1\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));

        Assert.Contains("not_a_real_setting", exception.Message);
    }

    [Fact]
    public void Load_TomlScalarAndListSettingsAbsentFromFile_UseDocumentedDefaults()
    {
        File.WriteAllText(TomlConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.Equal("0.0.0.0", settings.WebHost);
        Assert.Equal(3157, settings.WebPort);
        Assert.Equal(2.0, settings.MinRatio);
        Assert.Equal(["m4b", "m4a", "mp4", "mp3", "ogg"], settings.AudioTypes);
        Assert.Empty(settings.IgnoreTorrents);
    }

    [Fact]
    public void Load_InvalidTomlSyntax_ThrowsConfigurationException()
    {
        File.WriteAllText(TomlConfigPath, "this is not = = valid toml [[[");

        Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));
    }

    [Fact]
    public void Load_MissingTomlConfigFile_CreatesMinimalTomlFileAndStartupContinues()
    {
        Assert.False(File.Exists(TomlConfigPath));

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.True(File.Exists(TomlConfigPath));
        Assert.Equal("mam_id = \"\"\n", File.ReadAllText(TomlConfigPath));
        Assert.Equal(string.Empty, settings.MamId);
    }

    [Fact]
    public void Load_UnrecognizedExtension_ThrowsNamingTheExtension()
    {
        var path = Path.Combine(_tempDir, "config.ini");
        File.WriteAllText(path, "mam_id = \"\"\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(path));

        Assert.Contains(".ini", exception.Message);
    }

    [Theory]
    [InlineData("config.yaml")]
    [InlineData("config.yml")]
    public void Load_YamlExtension_ThrowsNamingTheExtension(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, "mam_id: \"\"\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(path));

        Assert.Contains(Path.GetExtension(fileName), exception.Message);
    }

    [Fact]
    public void ResolveDefaultConfigFilePath_TomlPresent_ReturnsTomlPath()
    {
        File.WriteAllText(TomlConfigPath, "mam_id = \"\"\n");

        var resolved = ConfigLoader.ResolveDefaultConfigFilePath(_tempDir);

        Assert.Equal(TomlConfigPath, resolved);
    }

    [Fact]
    public void ResolveDefaultConfigFilePath_TomlAbsent_StillReturnsTomlPath()
    {
        var resolved = ConfigLoader.ResolveDefaultConfigFilePath(_tempDir);

        Assert.Equal(TomlConfigPath, resolved);
    }

    [Fact]
    public void ResolveDefaultConfigFilePath_StrayYamlFilePresent_StillReturnsTomlPath()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), "mam_id: \"\"\n");

        var resolved = ConfigLoader.ResolveDefaultConfigFilePath(_tempDir);

        Assert.Equal(TomlConfigPath, resolved);
    }

    [Fact]
    public void Load_SingleSectionWrittenAsArrayOfTables_NamesTheFix()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = ""

            [[audiobookshelf]]
            url = "http://abs.local"
            token = "secret-token"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));

        Assert.Equal(
            "Invalid configuration: there can be only one 'audiobookshelf' section; write [audiobookshelf], not [[audiobookshelf]].",
            exception.Message);
    }

    [Fact]
    public void Load_RepeatableSectionWrittenAsSingleTable_NamesTheFix()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = ""

            [qbittorrent]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));

        Assert.Equal(
            "Invalid configuration: 'qbittorrent' is a repeatable section; write [[qbittorrent]], not [qbittorrent].",
            exception.Message);
    }

    [Fact]
    public void Load_NestedSingleSectionWrittenAsArrayOfTables_NamesTheFullPath()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = ""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [[qbittorrent.on_cleaned]]
            tags = ["superseded"]
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(TomlConfigPath));

        Assert.Equal(
            "Invalid configuration: there can be only one 'qbittorrent.on_cleaned' section; write [qbittorrent.on_cleaned], not [[qbittorrent.on_cleaned]].",
            exception.Message);
    }

    [Fact]
    public void Load_CategoryListsInsideRepeatedSections_AreNotMistakenForSections()
    {
        File.WriteAllText(TomlConfigPath, """
            mam_id = ""

            [[tag]]
            categories = { audio = false, ebook = ["Fantasy", "Science Fiction"] }
            category = "ebooks"
            """);

        var settings = ConfigLoader.Load(TomlConfigPath);

        Assert.Equal("ebooks", Assert.Single(settings.Tag).Category);
    }
}

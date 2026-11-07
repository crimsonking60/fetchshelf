using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class LibraryConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-library-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_LibrarySectionWithCategory_ParsesWithDefaultsApplied()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        var library = Assert.Single(settings.Library);
        Assert.Equal("/library/audiobooks", library.LibraryDir);
        Assert.Equal("audiobooks", library.Category);
        Assert.Null(library.DownloadDir);
        Assert.Equal(LibraryLinkMethod.HardlinkOrCopy, library.Method);
        Assert.Empty(library.AllowTags);
        Assert.Empty(library.DenyTags);
        Assert.Null(library.AudioTypes);
        Assert.Null(library.EbookTypes);
        Assert.True(library.MetadataJson);
        Assert.False(library.MetadataOpf);
    }

    [Fact]
    public void Load_GlobalMetadataSettings_ApplyToLibrariesWithoutTheirOwn()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""
            metadata_json = false
            metadata_opf = true

            [[library]]
            library_dir = "/library/ebooks"
            category = "ebooks"

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"
            metadata_json = true
            metadata_opf = false
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.False(settings.MetadataJson);
        Assert.True(settings.MetadataOpf);
        Assert.False(settings.Library[0].MetadataJson);
        Assert.True(settings.Library[0].MetadataOpf);
        Assert.True(settings.Library[1].MetadataJson);
        Assert.False(settings.Library[1].MetadataOpf);
    }

    [Fact]
    public void Load_LibraryMetadataFiles_AreReadPerLibrary()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library/ebooks"
            category = "ebooks"
            metadata_json = false
            metadata_opf = true
            """);

        var library = Assert.Single(ConfigLoader.Load(ConfigPath).Library);

        Assert.False(library.MetadataJson);
        Assert.True(library.MetadataOpf);
    }

    [Fact]
    public void Load_GlobalLinkMethod_AppliesToLibrariesWithoutTheirOwn()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""
            link_method = "hardlink_or_copy"

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"

            [[library]]
            library_dir = "/library/ebooks"
            category = "ebooks"
            method = "symlink"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(LibraryLinkMethod.HardlinkOrCopy, settings.LinkMethod);
        Assert.Equal(LibraryLinkMethod.HardlinkOrCopy, settings.Library[0].Method);
        Assert.Equal(LibraryLinkMethod.Symlink, settings.Library[1].Method);
    }

    [Fact]
    public void Load_LibraryMethodSetToHardlink_IsNotReplacedByGlobalLinkMethod()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""
            link_method = "copy"

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"
            method = "hardlink"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(LibraryLinkMethod.Hardlink, Assert.Single(settings.Library).Method);
    }

    [Fact]
    public void Load_LibrarySectionWithAllFields_ParsesEachField()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library/ebooks"
            download_dir = "/downloads/ebooks"
            name = "Ebooks"
            method = "hardlink_or_copy"
            allow_tags = ["fiction"]
            deny_tags = ["dnp"]
            audio_types = ["m4b"]
            ebook_types = ["epub"]
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        var library = Assert.Single(settings.Library);
        Assert.Equal("/downloads/ebooks", library.DownloadDir);
        Assert.Null(library.Category);
        Assert.Equal("Ebooks", library.Name);
        Assert.Equal(LibraryLinkMethod.HardlinkOrCopy, library.Method);
        Assert.Equal(["fiction"], library.AllowTags);
        Assert.Equal(["dnp"], library.DenyTags);
        Assert.Equal(["m4b"], library.AudioTypes);
        Assert.Equal(["epub"], library.EbookTypes);
    }

    [Fact]
    public void Load_LibrarySectionWithBothCategoryAndDownloadDir_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library"
            category = "audiobooks"
            download_dir = "/downloads"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("category", exception.Message);
        Assert.Contains("download_dir", exception.Message);
    }

    [Fact]
    public void Load_LibrarySectionWithNeitherCategoryNorDownloadDir_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("category", exception.Message);
        Assert.Contains("download_dir", exception.Message);
    }

    [Fact]
    public void Load_QBittorrentSectionWithOperationalFields_ParsesEachField()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [qbittorrent.path_mapping]
            "/data" = "/downloads"

            [qbittorrent.on_cleaned]
            category = "cleaned"
            tags = ["replaced"]

            [qbittorrent.on_unlinked]
            category = "unlinked"
            tags = ["not-wanted"]

            [qbittorrent.on_invalid_torrent]
            category = "invalid"
            tags = ["needs-review"]
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        var connection = Assert.Single(settings.QBittorrent);
        Assert.Equal("/downloads", connection.PathMapping["/data"]);
        Assert.Equal("cleaned", connection.OnCleaned?.Category);
        Assert.Equal(["replaced"], connection.OnCleaned?.Tags);
        Assert.Equal("unlinked", connection.OnUnlinked?.Category);
        Assert.Equal(["not-wanted"], connection.OnUnlinked?.Tags);
        Assert.Equal("invalid", connection.OnInvalidTorrent?.Category);
        Assert.Equal(["needs-review"], connection.OnInvalidTorrent?.Tags);
    }

    [Fact]
    public void Load_QBittorrentSectionWithoutOperationalFields_DefaultsToEmpty()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"
            """);

        var settings = ConfigLoader.Load(ConfigPath);

        var connection = Assert.Single(settings.QBittorrent);
        Assert.Empty(connection.PathMapping);
        Assert.Null(connection.OnCleaned);
        Assert.Null(connection.OnUnlinked);
        Assert.Null(connection.OnInvalidTorrent);
    }
}

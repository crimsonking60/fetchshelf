using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_ValidConfigFile_ParsesRecognizedTopLevelSettings()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"abc123\"\nmin_ratio = 1.5\nweb_port = 9000\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal("abc123", settings.MamId);
        Assert.Equal(1.5, settings.MinRatio);
        Assert.Equal(9000, settings.WebPort);
    }

    [Fact]
    public void Load_UnknownTopLevelKey_ThrowsNamingTheKey()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"abc123\"\nnot_a_real_setting = 1\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("not_a_real_setting", exception.Message);
    }

    [Fact]
    public void Load_ScalarSettingsAbsentFromFile_UseDocumentedDefaults()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal("0.0.0.0", settings.WebHost);
        Assert.Equal(3157, settings.WebPort);
        Assert.Equal(2.0, settings.MinRatio);
        Assert.Equal(10, settings.UnsatBuffer);
        Assert.Equal(0, settings.WedgeBuffer);
        Assert.False(settings.AddTorrentsStopped);
        Assert.False(settings.ExcludeNarratorInLibraryDir);
        Assert.False(settings.DatabaseWal);
        Assert.True(settings.MetadataJson);
        Assert.False(settings.MetadataOpf);
        Assert.True(settings.PageExplanations);
        Assert.True(settings.JobExplanations);
        Assert.Equal(30, settings.SearchInterval);
        Assert.Equal(10, settings.LinkInterval);
    }

    [Fact]
    public void Load_ListSettingsAbsentFromFile_UseDocumentedDefaults()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(["m4b", "m4a", "mp4", "mp3", "ogg"], settings.AudioTypes);
        Assert.Equal(["cbz", "epub", "pdf", "mobi", "azw3", "azw", "cbr"], settings.EbookTypes);
        Assert.Equal(["flac", "mp3", "pdf"], settings.MusicTypes);
        Assert.Equal(["mp3"], settings.RadioTypes);
        Assert.Empty(settings.IgnoreTorrents);
    }

    [Fact]
    public void Load_MissingConfigFile_CreatesMinimalFileAndStartupContinues()
    {
        Assert.False(File.Exists(ConfigPath));

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(string.Empty, settings.MamId);
    }

    [Fact]
    public void BackgroundJobsEnabled_IsFalse_WhenMamIdEmpty()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.False(settings.BackgroundJobsEnabled);
    }

    [Fact]
    public void BackgroundJobsEnabled_IsTrue_WhenMamIdSet()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"abc123\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.True(settings.BackgroundJobsEnabled);
    }

    [Fact]
    public void Load_PageExplanationsFalse_DisablesPageExplanations()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\npage_explanations = false\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.False(settings.PageExplanations);
    }

    [Fact]
    public void Load_JobExplanationsFalse_DisablesJobExplanations()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\njob_explanations = false\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.False(settings.JobExplanations);
    }

    [Theory]
    [InlineData("goodreads_interval = 42\n", "goodreads_interval")]
    [InlineData("[[goodreads_list]]\nurl = \"https://www.goodreads.com/review/list_rss/1?shelf=to-read\"\n", "goodreads_list")]
    public void Load_RemovedGoodreadsSettings_AreRejectedByName(string toml, string key)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public void Load_EnvironmentVariableAndFileValue_EnvironmentVariableWins()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"from-file\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id", "from-env");
        try
        {
            var settings = ConfigLoader.Load(ConfigPath);

            Assert.Equal("from-env", settings.MamId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id", null);
        }
    }

    [Fact]
    public void Load_TopLevelLanguagesFromEnvironmentVariable_ReachAutograbsWithoutTheirOwn()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n\n[[autograb]]\ntype = \"new\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_languages", "English,German");
        try
        {
            Assert.Equal(["English", "German"], Assert.Single(ConfigLoader.Load(ConfigPath).Autograb).Languages);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_languages", null);
        }
    }

    [Fact]
    public void Load_DatabaseWalOn_InFileOrFromEnvironmentVariable()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\ndatabase_wal = true\n");
        Assert.True(ConfigLoader.Load(ConfigPath).DatabaseWal);

        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_database_wal", "true");
        try
        {
            Assert.True(ConfigLoader.Load(ConfigPath).DatabaseWal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_database_wal", null);
        }
    }

    [Fact]
    public void Load_GlobalMetadataSettingsFromEnvironmentVariables_ReachLibrariesWithoutTheirOwn()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library/ebooks"
            category = "ebooks"
            """);
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_metadata_opf", "true");
        try
        {
            Assert.True(Assert.Single(ConfigLoader.Load(ConfigPath).Library).MetadataOpf);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_metadata_opf", null);
        }
    }

    [Fact]
    public void Load_MamIdFileFromEnvironmentVariable_ReadsTheFile()
    {
        var secretPath = Path.Combine(Path.GetDirectoryName(ConfigPath)!, "mam_id");
        File.WriteAllText(secretPath, "from-file\n");
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id_file", secretPath);
        try
        {
            Assert.Equal("from-file", ConfigLoader.Load(ConfigPath).MamId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id_file", null);
        }
    }

    [Fact]
    public void Load_MamIdFileFromEnvironmentVariableWithMamIdInFile_Throws()
    {
        var secretPath = Path.Combine(Path.GetDirectoryName(ConfigPath)!, "mam_id");
        File.WriteAllText(secretPath, "from-file");
        File.WriteAllText(ConfigPath, "mam_id = \"from-config\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id_file", secretPath);
        try
        {
            Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_mam_id_file", null);
        }
    }

    [Fact]
    public void Load_EnvironmentVariableOnly_OverridesDefaultValue()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_min_ratio", "3.5");
        try
        {
            var settings = ConfigLoader.Load(ConfigPath);

            Assert.Equal(3.5, settings.MinRatio);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_min_ratio", null);
        }
    }

    [Fact]
    public void Load_LinkMethodFromEnvironment_AppliesToLibrariesWithoutTheirOwn()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"
            """);
        Environment.SetEnvironmentVariable("FETCHSHELF_CONF_link_method", "hardlink_or_copy");
        try
        {
            var settings = ConfigLoader.Load(ConfigPath);

            Assert.Equal(LibraryLinkMethod.HardlinkOrCopy, Assert.Single(settings.Library).Method);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FETCHSHELF_CONF_link_method", null);
        }
    }

    [Fact]
    public void Load_DownloadAfterSelection_IsOnByDefault_AndCanBeTurnedOff()
    {
        var dir = Directory.CreateTempSubdirectory("fetchshelf-download-after-selection-").FullName;
        try
        {
            var path = Path.Combine(dir, "config.toml");
            File.WriteAllText(path, "mam_id = \"\"\n");
            Assert.True(ConfigLoader.Load(path).DownloadAfterSelection);

            File.WriteAllText(path, "mam_id = \"\"\ndownload_after_selection = false\n");
            Assert.False(ConfigLoader.Load(path).DownloadAfterSelection);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

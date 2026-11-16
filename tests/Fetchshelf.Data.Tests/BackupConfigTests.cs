using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[backup]` section.</summary>
public sealed class BackupConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-backup-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_NoSection_TakesNoBackups()
    {
        Assert.Null(Load("").Backup);
    }

    [Fact]
    public void Load_OnlyAFolder_HasTheDefaults()
    {
        var backup = Load("[backup]\nfolder = \"backups\"\n").Backup!;

        Assert.Equal(("backups", 1440, 7, true), (backup.Folder, backup.Interval, backup.Keep, backup.BeforeUpgrade));
    }

    [Fact]
    public void Load_FullSection_ReadsEverySetting()
    {
        var backup = Load("""
            [backup]
            folder = "/mnt/other-disk/fetchshelf"
            interval = 0
            keep = 30
            before_upgrade = false
            """).Backup!;

        Assert.Equal(("/mnt/other-disk/fetchshelf", 0, 30, false), (backup.Folder, backup.Interval, backup.Keep, backup.BeforeUpgrade));
    }

    [Theory]
    [InlineData("", "the [backup] section needs a 'folder'")]
    [InlineData("folder = \" \"", "the [backup] section needs a 'folder'")]
    [InlineData("folder = \"b\"\ninterval = -1", "'interval' in the [backup] section must be between 0")]
    [InlineData("folder = \"b\"\ninterval = 50000", "'interval' in the [backup] section must be between 0")]
    [InlineData("folder = \"b\"\nkeep = 0", "'keep' in the [backup] section must be between 1 and 365; got 0.")]
    [InlineData("folder = \"b\"\nkeep = 366", "'keep' in the [backup] section must be between 1 and 365; got 366.")]
    [InlineData("folder = \"b\"\nkeep_last = 3", "keep_last")]
    public void Load_InvalidValue_StopsStartup(string body, string expected)
    {
        Assert.Contains(expected, LoadFails("[backup]\n" + body + "\n").Message);
    }

    [Fact]
    public void ResolveFolder_RelativeFolder_IsUnderTheDataFilesFolder()
    {
        var backup = new BackupSettings { Folder = "backups" };

        Assert.Equal(Path.Combine(_tempDir, "backups"), backup.ResolveFolder(Path.Combine(_tempDir, "data.db")));
    }

    [Fact]
    public void ResolveFolder_AbsoluteFolder_IsUsedAsItIs()
    {
        var elsewhere = Path.Combine(_tempDir, "other-disk", "fetchshelf");

        Assert.Equal(elsewhere, new BackupSettings { Folder = elsewhere }.ResolveFolder(Path.Combine(_tempDir, "database", "data.db")));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("backups/..")]
    public void ResolveFolder_TheDataFilesOwnFolder_IsRefused(string folder)
    {
        var error = Assert.Throws<ConfigurationException>(() => new BackupSettings { Folder = folder }.ResolveFolder(Path.Combine(_tempDir, "data.db")));

        Assert.Contains("[backup] 'folder' is the database's own folder", error.Message);
    }

    [Fact]
    public void ResolveFolder_TheDataFilesOwnFolderWrittenInFull_IsRefused()
    {
        Assert.Throws<ConfigurationException>(() => new BackupSettings { Folder = _tempDir + "/" }.ResolveFolder(Path.Combine(_tempDir, "data.db")));
    }

    [Fact]
    public void Load_ABlockNamedBackup_IsRefused()
    {
        Assert.Contains("Backup", LoadFails("[[autograb]]\nname = \"backup\"\ntype = \"bookmarks\"\n").Message);
    }
}

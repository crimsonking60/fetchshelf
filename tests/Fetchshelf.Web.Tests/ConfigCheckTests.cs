namespace Fetchshelf.Web.Tests;

public sealed class ConfigCheckTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-config-check-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private (int ExitCode, string Output) Check(string? toml)
    {
        if (toml is not null)
        {
            File.WriteAllText(ConfigPath, toml);
        }

        using var output = new StringWriter();
        var exitCode = ConfigCheck.Run(ConfigPath, output);
        return (exitCode, output.ToString());
    }

    private const string FullConfig = """
        mam_id = "secret-session-id"
        min_ratio = 1.5
        search_interval = 20
        link_interval = 5

        [[qbittorrent]]
        url = "http://qbittorrent:8080"
        username = "admin"
        password = "secret-password"

        [qbittorrent.path_mapping]
        "/downloads" = "/data/downloads"

        [[library]]
        name = "audiobooks"
        category = "audiobooks"
        library_dir = "/library/audiobooks"

        [[library]]
        download_dir = "/data/ebooks"
        library_dir = "/library/ebooks"
        metadata_opf = true

        [[autograb]]
        name = "Bookmarks"
        type = "bookmarks"

        [[autograb]]
        type = { uploader = 42 }
        cost = "try_wedge"
        search_interval = 0
        dry_run = true

        [hardcover]
        token = "secret-token"

        [[hardcover_list]]
        name = "Want to read"
        status = "want_to_read"

        [[hardcover_list.grab]]
        cost = "ratio"

        [[tag]]
        category = "audiobooks"

        [audiobookshelf]
        url = "http://abs"
        token = "secret-abs-token"

        [opds]
        libraries = ["audiobooks"]
        """;

    [Fact]
    public void Run_GoodFile_ExitsWithZeroAndListsTheJobsAsTheJobsPageNamesThem()
    {
        var (exitCode, output) = Check(FullConfig);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Configuration OK: {ConfigPath}", output);
        Assert.Contains("MAM: mam_id set; background jobs run", output);
        Assert.Contains("Safety: min_ratio 1.5, unsat_buffer 10, wedge_buffer 0", output);
        Assert.Contains("http://qbittorrent:8080 (1 path mapping)", output);
        Assert.Matches(@"audiobooks\s+category audiobooks\s+-> /library/audiobooks  hardlink_or_copy, metadata\.json", output);
        Assert.Matches(@"library 2\s+download_dir /data/ebooks\s+-> /library/ebooks  hardlink_or_copy, metadata\.json, metadata\.opf", output);
        Assert.Matches(@"Link\s+every 5 min", output);
        Assert.Matches(@"Clean\s+after every Link run", output);
        Assert.Matches(@"Audiobookshelf\s+every 10 min", output);
        Assert.Matches(@"Bookmarks\s+autograb bookmarks, cost free, every 20 min", output);
        Assert.Matches(@"autograb\[1\]\s+autograb uploader 42, cost try_wedge, only from the Jobs page, dry run", output);
        Assert.Matches(@"Want to read\s+Hardcover want_to_read, 1 grab block, every 20 min", output);
        Assert.Matches(@"Download\s+every 20 min, and after selections", output);
        Assert.Contains("Tag rules: 1", output);
        Assert.Contains("Audiobookshelf: http://abs", output);
        Assert.Contains("Hardcover: token set", output);
        Assert.Contains("OPDS: audiobooks", output);
    }

    [Fact]
    public void Run_GoodFile_NeverPrintsASecret()
    {
        var (_, output) = Check(FullConfig);

        Assert.DoesNotContain("secret", output);
        Assert.DoesNotContain("admin", output);
    }

    [Fact]
    public void Run_NoMamId_SaysNoJobsRun()
    {
        var (exitCode, output) = Check("mam_id = \"\"\n");

        Assert.Equal(0, exitCode);
        Assert.Contains("no mam_id; the web UI runs, but no background job does", output);
        Assert.Contains("Jobs: none without mam_id", output);
        Assert.Contains("Libraries: none", output);
    }

    [Fact]
    public void Run_BadFile_ExitsWithOneAndTheReasonStartupWouldGive()
    {
        var (exitCode, output) = Check("mam_id = \"x\"\nmin_ratio = 0\n");

        Assert.Equal(1, exitCode);
        Assert.Contains($"Fetchshelf would not start with {ConfigPath}: Invalid configuration: 'min_ratio' must be a number above 0", output);
    }

    [Fact]
    public void Run_BackupSection_IsSummedUp()
    {
        var (exitCode, output) = Check("mam_id = \"\"\n[backup]\nfolder = \"backups\"\nkeep = 14\n");

        Assert.Equal(0, exitCode);
        Assert.Contains("Backup: to backups, every 1440 min, keep 14, and before upgrades", output);
    }

    [Fact]
    public void Run_BackupFolderIsTheDatabasesOwnFolder_ExitsWithOne()
    {
        var (exitCode, output) = Check("mam_id = \"\"\n[backup]\nfolder = \".\"\n");

        Assert.Equal(1, exitCode);
        Assert.Contains("[backup] 'folder' is the database's own folder", output);
    }

    [Fact]
    public void Run_MissingFile_ExitsWithOneWithoutCreatingIt()
    {
        var (exitCode, output) = Check(toml: null);

        Assert.Equal(1, exitCode);
        Assert.Contains($"No configuration file at {ConfigPath}", output);
        Assert.False(File.Exists(ConfigPath));
    }

    private const string FileListConfig = """
        mam_id = "session"

        [[file_list]]
        name = "Goodreads"
        path = "lists/export.csv"
        shelf = "to-read"

        [[file_list.grab]]
        """;

    [Fact]
    public void Run_FileListWhoseFileIsMissing_SaysSo_AndStillExitsWithZero()
    {
        var (exitCode, output) = Check(FileListConfig);

        Assert.Equal(0, exitCode);
        Assert.Contains("file lists/export.csv, shelf 'to-read', 1 grab block", output);
        Assert.Contains("Goodreads: List file not found: lists/export.csv", output);
        Assert.Contains("the job fails until it can be read", output);
    }

    [Fact]
    public void Run_FileListWithItsFile_SaysHowManyBooksItFound()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "lists"));
        File.WriteAllText(Path.Combine(_tempDir, "lists", "export.csv"),
            "Title,Author,Exclusive Shelf\nDune,Frank Herbert,to-read\nHyperion,Dan Simmons,read\n,Nobody,to-read\n");

        var (exitCode, output) = Check(FileListConfig);

        Assert.Equal(0, exitCode);
        Assert.Contains("Goodreads: lists/export.csv: 1 book (CSV); lines not read: 4 (it has no title)", output);
    }
}

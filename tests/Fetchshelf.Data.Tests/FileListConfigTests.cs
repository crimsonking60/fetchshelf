using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[[file_list]]` sections.</summary>
public sealed class FileListConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-file-list-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_FullEntry_ReadsEverySetting()
    {
        var settings = Load("""
            mam_id = ""

            [[file_list]]
            name = "Goodreads to-read"
            path = "lists/goodreads_library_export.csv"
            shelf = "to-read"
            prefer_format = "audio"
            category = "audiobooks"
            search_interval = 60
            unsat_buffer = 5
            wedge_buffer = 2
            max_active_downloads = 3
            dry_run = true

            [[file_list.grab]]
            cost = "free"
            """);

        var list = Assert.Single(settings.FileList);
        Assert.Equal("Goodreads to-read", list.Name);
        Assert.Equal("lists/goodreads_library_export.csv", list.Path);
        Assert.Equal(Path.Combine(_tempDir, "lists", "goodreads_library_export.csv"), list.FullPath);
        Assert.Equal("to-read", list.Shelf);
        Assert.Equal(FilterMainType.Audio, list.PreferFormat);
        Assert.Equal("audiobooks", list.Category);
        Assert.Equal(60, list.SearchInterval);
        Assert.Equal((5, 2, 3), (list.UnsatBuffer!.Value, list.WedgeBuffer!.Value, list.MaxActiveDownloads!.Value));
        Assert.True(list.DryRun);
        Assert.Equal(ConfiguredCost.Free, Assert.Single(list.Grab).Cost);
    }

    [Fact]
    public void Load_MinimalEntry_HasDefaults_AndANameFromItsIndex()
    {
        var settings = Load("""
            [[file_list]]
            path = "wanted.txt"

            [[file_list.grab]]
            """);

        var list = Assert.Single(settings.FileList);
        Assert.Null(list.Shelf);
        Assert.Null(list.PreferFormat);
        Assert.Null(list.SearchInterval);
        Assert.False(list.DryRun);
        Assert.Equal("file_list[0]", AutoDownloaderJobNames.FileList(list, 0));
    }

    [Fact]
    public void Load_AbsolutePath_IsKept()
    {
        var absolute = Path.Combine(_tempDir, "elsewhere", "list.csv");
        var settings = Load($"""
            [[file_list]]
            path = '{absolute}'

            [[file_list.grab]]
            """);

        Assert.Equal(absolute, settings.FileList[0].FullPath);
    }

    [Fact]
    public void Load_MissingFile_IsNoError()
    {
        var settings = Load("""
            [[file_list]]
            path = "not-there-yet.csv"

            [[file_list.grab]]
            """);

        Assert.False(File.Exists(settings.FileList[0].FullPath));
    }

    [Fact]
    public void Load_WithoutPath_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            name = "Wanted"

            [[file_list.grab]]
            """);

        Assert.Contains("the [[file_list]] 'Wanted' needs a 'path'", error.Message);
    }

    [Fact]
    public void Load_NoGrabBlock_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            path = "wanted.txt"
            """);

        Assert.Contains("a [[file_list]] section must have at least one 'grab' block", error.Message);
    }

    [Fact]
    public void Load_MetadataOnlyCost_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            path = "wanted.txt"

            [[file_list.grab]]
            cost = "metadata_only"
            """);

        Assert.Contains("metadata-only 'cost'", error.Message);
    }

    [Fact]
    public void Load_UnknownKey_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            path = "wanted.txt"
            url = "https://example.com/list.csv"

            [[file_list.grab]]
            """);

        Assert.Contains("url", error.Message);
    }

    [Fact]
    public void Load_FullPathInTheFile_IsRejectedAsUnknown()
    {
        LoadFails("""
            [[file_list]]
            path = "wanted.txt"
            full_path = "/etc/passwd"

            [[file_list.grab]]
            """);
    }

    [Fact]
    public void Load_SameNameAsAHardcoverList_Throws()
    {
        var error = LoadFails("""
            [hardcover]
            token = "t"

            [[hardcover_list]]
            name = "Wanted"
            status = "want_to_read"

            [[hardcover_list.grab]]

            [[file_list]]
            name = "wanted"
            path = "wanted.txt"

            [[file_list.grab]]
            """);

        Assert.Contains("have the same name", error.Message);
    }

    [Fact]
    public void Load_GeneratedNameForm_IsRefusedAsAName()
    {
        var error = LoadFails("""
            [[file_list]]
            name = "file_list[3]"
            path = "wanted.txt"

            [[file_list.grab]]
            """);

        Assert.Contains("given to blocks without a name", error.Message);
    }

    [Fact]
    public void Load_SearchIntervalOutOfRange_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            path = "wanted.txt"
            search_interval = -1

            [[file_list.grab]]
            """);

        Assert.Contains("'search_interval' in the 1st [[file_list]] section", error.Message);
    }
}

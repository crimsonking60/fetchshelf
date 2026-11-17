using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[[bookmarks_list]]` section.</summary>
public sealed class BookmarksListConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-bookmarks-list-config-tests-").FullName;

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
            [[bookmarks_list]]
            name = "MAM bookmarks"
            category = "audiobooks"
            search_interval = 60
            unsat_buffer = 5
            wedge_buffer = 2
            max_active_downloads = 3
            dry_run = true

            [[bookmarks_list.grab]]
            cost = "free"

            [[bookmarks_list.grab]]
            cost = "wedge"
            """);

        var list = Assert.Single(settings.BookmarksList);
        Assert.Equal(("MAM bookmarks", "audiobooks", 60), (list.Name, list.Category, list.SearchInterval!.Value));
        Assert.Equal((5, 2, 3), (list.UnsatBuffer!.Value, list.WedgeBuffer!.Value, list.MaxActiveDownloads!.Value));
        Assert.True(list.DryRun);
        Assert.Equal([ConfiguredCost.Free, ConfiguredCost.Wedge], list.Grab.Select(g => g.Cost));
        Assert.Equal("MAM bookmarks", AutoDownloaderJobNames.BookmarksList(list));
    }

    [Fact]
    public void Load_Unnamed_IsCalledBookmarksList()
    {
        var settings = Load("[[bookmarks_list]]\n\n[[bookmarks_list.grab]]\n");

        Assert.Equal("bookmarks_list", AutoDownloaderJobNames.BookmarksList(Assert.Single(settings.BookmarksList)));
    }

    [Fact]
    public void Load_TwoBookmarksLists_Throws()
    {
        var error = LoadFails("""
            [[bookmarks_list]]
            name = "One"

            [[bookmarks_list.grab]]

            [[bookmarks_list]]
            name = "Two"

            [[bookmarks_list.grab]]
            """);

        Assert.Contains("there can be only one [[bookmarks_list]] section", error.Message);
    }

    [Fact]
    public void Load_TogetherWithABookmarksAutograb_Throws()
    {
        var error = LoadFails("""
            [[autograb]]
            name = "Bookmarks"
            type = "bookmarks"

            [[bookmarks_list]]

            [[bookmarks_list.grab]]
            """);

        Assert.Contains("the [[bookmarks_list]] section and the [[autograb]] 'Bookmarks' would both select the bookmarked torrents", error.Message);
    }

    [Fact]
    public void Load_TogetherWithAnotherAutograb_IsFine()
    {
        var settings = Load("""
            [[autograb]]
            type = "freeleech"

            [[bookmarks_list]]

            [[bookmarks_list.grab]]
            """);

        Assert.Single(settings.BookmarksList);
    }

    [Fact]
    public void Load_PreferFormat_Throws()
    {
        var error = LoadFails("""
            [[bookmarks_list]]
            prefer_format = "audio"

            [[bookmarks_list.grab]]
            """);

        Assert.Contains("can't have 'prefer_format': a bookmark is one torrent, of one format already", error.Message);
    }

    [Fact]
    public void Load_NoGrabBlock_Throws()
    {
        var error = LoadFails("[[bookmarks_list]]\nname = \"Marks\"\n");

        Assert.Contains("the [[bookmarks_list]] 'Marks' must have at least one 'grab' block", error.Message);
    }

    [Fact]
    public void Load_NamedLikeAnUnnamedOne_Throws()
    {
        var error = LoadFails("""
            [[file_list]]
            name = "bookmarks_list"
            path = "x.txt"

            [[file_list.grab]]
            """);

        Assert.Contains("given to blocks without a name", error.Message);
    }
}

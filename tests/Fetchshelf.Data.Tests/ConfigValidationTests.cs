using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ConfigValidationTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-config-validation-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private string Rejected(string toml) => Assert.Throws<ConfigurationException>(() => Load(toml)).Message;

    [Theory]
    [InlineData("min_ratio = 0.01")]
    [InlineData("web_port = 1")]
    [InlineData("web_port = 65535")]
    [InlineData("search_interval = 0")]
    [InlineData("search_interval = 43200")]
    [InlineData("link_interval = 0")]
    [InlineData("unsat_buffer = 0")]
    [InlineData("wedge_buffer = 0")]
    [InlineData("ignore_torrents = [\"1\", \"123456\"]")]
    public void Load_TopLevelValueAtItsBoundary_IsAccepted(string line)
    {
        Load(line + "\n");
    }

    [Theory]
    [InlineData("min_ratio = 0", "'min_ratio'")]
    [InlineData("min_ratio = -1", "'min_ratio'")]
    [InlineData("min_ratio = inf", "'min_ratio'")]
    [InlineData("min_ratio = nan", "'min_ratio'")]
    [InlineData("web_port = 0", "'web_port'")]
    [InlineData("web_port = 65536", "'web_port'")]
    [InlineData("search_interval = -1", "'search_interval'")]
    [InlineData("search_interval = 43201", "'search_interval'")]
    [InlineData("link_interval = -5", "'link_interval'")]
    [InlineData("unsat_buffer = -1", "'unsat_buffer'")]
    [InlineData("wedge_buffer = -1", "'wedge_buffer'")]
    [InlineData("ignore_torrents = [\"12a\"]", "'12a'")]
    [InlineData("ignore_torrents = [\"0\"]", "'0'")]
    [InlineData("ignore_torrents = [\"-5\"]", "'-5'")]
    public void Load_TopLevelValueOutOfRange_IsRejectedNamingTheSetting(string line, string expected)
    {
        var message = Rejected(line + "\n");

        Assert.StartsWith("Invalid configuration: ", message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public void Load_AudiobookshelfIntervalOutOfRange_IsRejectedNamingTheSection()
    {
        var message = Rejected("""

            [audiobookshelf]
            url = "http://abs"
            token = "t"
            interval = -1
            """);

        Assert.Contains("'interval' in the [audiobookshelf] section", message);
    }

    [Theory]
    [InlineData("search_interval = -1", "'search_interval'")]
    [InlineData("search_interval = 50000", "'search_interval'")]
    [InlineData("unsat_buffer = -1", "'unsat_buffer'")]
    [InlineData("wedge_buffer = -1", "'wedge_buffer'")]
    [InlineData("max_active_downloads = 0", "'max_active_downloads'")]
    [InlineData("max_pages = 0", "'max_pages'")]
    public void Load_AutograbValueOutOfRange_IsRejectedNamingTheBlock(string line, string expected)
    {
        var message = Rejected($"""

            [[autograb]]
            name = "Bookmarks"
            type = "bookmarks"
            {line}
            """);

        Assert.Contains($"{expected} in the [[autograb]] 'Bookmarks'", message);
    }

    [Fact]
    public void Load_MarkRemovedOnBookmarks_IsRejected()
    {
        var message = Rejected("""

            [[autograb]]
            name = "Bookmarks"
            type = "bookmarks"
            mark_removed = true
            """);

        Assert.Equal(
            "Invalid configuration: the [[autograb]] 'Bookmarks' sets 'mark_removed', which needs a search by id range; bookmark lists aren't one.",
            message);
    }

    [Fact]
    public void Load_MarkRemovedOnASearch_IsAccepted()
    {
        var settings = Load("""

            [[autograb]]
            type = "freeleech"
            mark_removed = true
            """);

        Assert.True(Assert.Single(settings.Autograb).MarkRemoved);
    }

    [Theory]
    [InlineData("on_cleaned")]
    [InlineData("on_unlinked")]
    [InlineData("on_invalid_torrent")]
    public void Load_CategoryAndTagsTableSettingNeither_IsRejected(string setting)
    {
        var message = Rejected($"""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [qbittorrent.{setting}]
            tags = []
            """);

        Assert.Equal(
            $"Invalid configuration: '{setting}' in the 1st [[qbittorrent]] section sets neither a 'category' nor 'tags', so it would do nothing; set one or remove the table.",
            message);
    }

    [Theory]
    [InlineData("category = \"invalid\"")]
    [InlineData("tags = [\"needs-review\"]")]
    public void Load_OnInvalidTorrentWithCategoryOrTags_IsAccepted(string line)
    {
        var settings = Load($"""

            [[qbittorrent]]
            url = "http://localhost:8080"
            username = "admin"
            password = "adminadmin"

            [qbittorrent.on_invalid_torrent]
            {line}
            """);

        Assert.NotNull(Assert.Single(settings.QBittorrent).OnInvalidTorrent);
    }

    [Fact]
    public void Load_UnnamedBlockOutOfRange_IsNamedByItsPosition()
    {
        var message = Rejected("""

            [[autograb]]
            type = "new"

            [[autograb]]
            type = "new"
            max_pages = 0
            """);

        Assert.Contains("the 2nd [[autograb]] section", message);
    }

    [Theory]
    [InlineData("[[snatchlist]]\ntype = \"unsat\"\nsearch_interval = -1", "'search_interval' in the 1st [[snatchlist]] section")]
    [InlineData("[[snatchlist]]\ntype = \"unsat\"\nmax_pages = 0", "'max_pages' in the 1st [[snatchlist]] section")]
    public void Load_SnatchlistValueOutOfRange_IsRejected(string block, string expected)
    {
        Assert.Contains(expected, Rejected("\n" + block + "\n"));
    }

    [Fact]
    public void Load_HardcoverListIntervalOutOfRange_IsRejected()
    {
        var message = Rejected("""

            [hardcover]
            token = "t"

            [[hardcover_list]]
            name = "Want to read"
            status = "want_to_read"
            search_interval = -1

            [[hardcover_list.grab]]
            """);

        Assert.Contains("'search_interval' in the [[hardcover_list]] 'Want to read'", message);
    }

    [Theory]
    [InlineData("min_seeders = -1", "'min_seeders'")]
    [InlineData("min_seeders = 5\nmax_seeders = 4", "'min_seeders' (5) above its 'max_seeders' (4)")]
    [InlineData("min_leechers = 3\nmax_leechers = 1", "'min_leechers' (3) above its 'max_leechers' (1)")]
    [InlineData("min_snatched = 3\nmax_snatched = 1", "'min_snatched' (3) above its 'max_snatched' (1)")]
    [InlineData("max_authors = 0", "'max_authors'")]
    [InlineData("min_size = \"2 GiB\"\nmax_size = \"1 GiB\"", "'min_size' (2 GiB) above its 'max_size' (1 GiB)")]
    [InlineData("uploaded_after = \"2024-02-01\"\nuploaded_before = \"2024-01-01\"", "'uploaded_after' (2024-02-01) later than")]
    public void Load_FilterBoundsThatContradict_AreRejected(string lines, string expected)
    {
        var message = Rejected($"""

            [[autograb]]
            name = "Bookmarks"
            type = "bookmarks"
            {lines}
            """);

        Assert.Contains("the [[autograb]] 'Bookmarks'", message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public void Load_FilterBoundsThatAgree_AreAccepted()
    {
        Load("""

            [[autograb]]
            type = "bookmarks"
            min_seeders = 1
            max_seeders = 1
            min_size = "1 GiB"
            max_size = "1 GiB"
            uploaded_after = "2024-01-01"
            uploaded_before = "2024-01-01"
            max_authors = 1
            """);
    }

    [Theory]
    [InlineData("languages = [\"Englsh\"]", "unrecognized language 'Englsh'")]
    [InlineData("media_type = [\"audiobok\"]", "unrecognized media type 'audiobok'")]
    [InlineData("categories = { audio = [\"Fantasyy\"] }", "unrecognized category name 'Fantasyy'")]
    [InlineData("flags = { lang = true }", "unrecognized flag name 'lang'")]
    [InlineData("min_size = \"5 gigs\"", "unrecognized size '5 gigs'")]
    [InlineData("max_size = \"1.2.3 GiB\"", "unrecognized size '1.2.3 GiB'")]
    [InlineData("uploaded_after = \"01-02-2024\"", "unrecognized date '01-02-2024'")]
    public void Load_BadFilterValueInAnAutograb_IsRejectedAtStartupNamingTheBlock(string line, string expected)
    {
        var message = Rejected($"""

            [[autograb]]
            name = "Bookmarks"
            type = "bookmarks"
            {line}
            """);

        Assert.Equal($"Invalid configuration: the [[autograb]] 'Bookmarks': {expected}", message.TrimEnd('.').Split(", expected")[0]);
    }

    [Fact]
    public void Load_BadFilterValueInASnatchlist_IsRejectedAtStartup()
    {
        var message = Rejected("""

            [[snatchlist]]
            type = "unsat"
            media_type = ["audiobok"]
            """);

        Assert.Contains("the 1st [[snatchlist]] section: unrecognized media type 'audiobok'", message);
    }

    [Fact]
    public void Load_BadFilterValueInAHardcoverGrabBlock_IsRejectedAtStartup()
    {
        var message = Rejected("""

            [hardcover]
            token = "t"

            [[hardcover_list]]
            name = "Want to read"
            status = "want_to_read"

            [[hardcover_list.grab]]

            [[hardcover_list.grab]]
            min_size = "big"
            """);

        Assert.Contains("grab block 2 of the [[hardcover_list]] 'Want to read': unrecognized size 'big'", message);
    }

    [Fact]
    public void Load_BadFilterValueInATagRule_IsRejectedAsAConfigurationError()
    {
        var message = Rejected("""

            [[tag]]
            categories = { ebook = ["Fantasyy"] }
            category = "ebooks"
            """);

        Assert.Contains("the 1st [[tag]] section: unrecognized category name 'Fantasyy'", message);
    }

    [Fact]
    public void Load_UnknownTopLevelLanguage_IsStillRejectedAsBefore()
    {
        var message = Rejected("languages = [\"Klingonese\"]\n\n[[autograb]]\ntype = \"new\"\n");

        Assert.Contains("unrecognized language 'Klingonese' in the top-level 'languages'", message);
    }

    [Fact]
    public void Load_WedgeOver_IsReadIntoBytes()
    {
        var settings = Load("\n[search]\nwedge_over = \"1 GiB\"\n");

        Assert.Equal(1024L * 1024 * 1024, settings.Search.WedgeOverBytes);
    }

    [Fact]
    public void Load_UnreadableWedgeOver_IsRejected()
    {
        Assert.Contains("[search] 'wedge_over' is 'huge'", Rejected("\n[search]\nwedge_over = \"huge\"\n"));
    }

    [Fact]
    public void Load_WedgeOverBytesInTheFile_IsAnUnknownKey()
    {
        Assert.Throws<ConfigurationException>(() => Load("\n[search]\nwedge_over_bytes = 5\n"));
    }

    [Theory]
    [InlineData("search_interval", "ten", "FETCHSHELF_CONF_search_interval is 'ten'; expected a whole number.")]
    [InlineData("web_port", "99999999999", "FETCHSHELF_CONF_web_port is '99999999999'; expected a whole number.")]
    [InlineData("min_ratio", "two", "FETCHSHELF_CONF_min_ratio is 'two'; expected a number such as 1.5.")]
    [InlineData("add_torrents_stopped", "yes", "FETCHSHELF_CONF_add_torrents_stopped is 'yes'; expected true or false.")]
    [InlineData(
        "link_method", "hard",
        "FETCHSHELF_CONF_link_method is 'hard'; expected one of hardlink, hardlink_or_copy, hardlink_or_symlink, copy, symlink, no_link.")]
    [InlineData(
        "link_method", "2",
        "FETCHSHELF_CONF_link_method is '2'; expected one of hardlink, hardlink_or_copy, hardlink_or_symlink, copy, symlink, no_link.")]
    public void Load_UnreadableEnvironmentOverride_IsRejectedNamingTheVariable(string setting, string value, string expected)
    {
        WithEnvironment(setting, value, () => Assert.Equal($"Invalid configuration: {expected}", Rejected("")));
    }

    [Theory]
    [InlineData("search_interval", "15")]
    [InlineData("min_ratio", "1.5")]
    [InlineData("add_torrents_stopped", "True")]
    [InlineData("link_method", "hardlink_or_copy")]
    [InlineData("link_method", "HardlinkOrCopy")]
    public void Load_ReadableEnvironmentOverride_IsAcceptedAsBefore(string setting, string value)
    {
        WithEnvironment(setting, value, () => Load(""));
    }

    [Fact]
    public void Load_EnvironmentOverrideOutOfRange_IsRejectedByTheRangeChecks()
    {
        WithEnvironment("min_ratio", "0", () => Assert.Contains("'min_ratio' must be a number above 0", Rejected("")));
    }

    private static void WithEnvironment(string setting, string value, Action action)
    {
        var variable = "FETCHSHELF_CONF_" + setting;
        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}

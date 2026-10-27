using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class GoodreadsListConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-goodreads-list-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MinimalEntry_ParsesFields()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
            prefer_format = "audio"

            [[goodreads_list.grab]]
            cost = "free"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).GoodreadsList);

        Assert.Equal("https://www.goodreads.com/review/list_rss/1?shelf=to-read", entry.Url);
        Assert.Equal(FilterMainType.Audio, entry.PreferFormat);
        Assert.Single(entry.Grab);
        Assert.Null(entry.SearchInterval);
    }

    [Fact]
    public void Load_MissingUrl_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[goodreads_list]]
            [[goodreads_list.grab]]
            cost = "free"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("url", exception.Message);
    }

    [Fact]
    public void Load_NoGrabBlocks_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("grab", exception.Message);
    }

    [Fact]
    public void Load_PreferFormatMusicology_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
            prefer_format = "musicology"

            [[goodreads_list.grab]]
            cost = "free"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("prefer_format", exception.Message);
    }

    [Fact]
    public void Load_UnknownKeyInEntry_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
            not_a_real_field = 1

            [[goodreads_list.grab]]
            cost = "free"
            """);

        Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    [Fact]
    public void Load_EntryWithoutOwnInterval_UsesGlobalImportInterval()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""
            import_interval = 60

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"

            [[goodreads_list.grab]]
            cost = "free"
            """);

        var settings = ConfigLoader.Load(ConfigPath);
        var entry = Assert.Single(settings.GoodreadsList);

        Assert.Null(entry.SearchInterval);
        Assert.Equal(60, settings.ImportInterval);
    }
}

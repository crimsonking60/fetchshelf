using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class JobNameConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-job-name-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private string Rejected(string toml) => Assert.Throws<ConfigurationException>(() => Load(toml)).Message;

    private static string Autograb(string? name) => name is null
        ? "\n[[autograb]]\ntype = \"new\"\n"
        : $"\n[[autograb]]\nname = \"{name}\"\ntype = \"new\"\n";

    private static string Snatchlist(string name) => $"\n[[snatchlist]]\nname = \"{name}\"\ntype = \"unsat\"\n";

    private static string HardcoverList(string name) => $"""

        [hardcover]
        token = "t"

        [[hardcover_list]]
        name = "{name}"
        status = "want_to_read"

        [[hardcover_list.grab]]

        """;

    [Fact]
    public void Load_TwoAutograbsWithOneName_IsRejectedNamingBoth()
    {
        var message = Rejected(Autograb("Bookmarks") + Autograb("Bookmarks"));

        Assert.Equal(
            "Invalid configuration: the [[autograb]] 'Bookmarks' and the [[autograb]] 'Bookmarks' have the same name; " +
            "every autograb, snatchlist and Hardcover list needs its own.",
            message);
    }

    [Fact]
    public void Load_SameNameOnAnAutograbAndAHardcoverList_IsRejected()
    {
        var message = Rejected(Autograb("Want to read") + HardcoverList("Want to read"));

        Assert.Contains("the [[autograb]] 'Want to read' and the [[hardcover_list]] 'Want to read' have the same name", message);
    }

    [Fact]
    public void Load_SameNameOnAnAutograbAndASnatchlist_IsRejected()
    {
        Assert.Contains("have the same name", Rejected(Autograb("Mine") + Snatchlist("Mine")));
    }

    [Fact]
    public void Load_NamesDifferingOnlyInCase_AreRejected()
    {
        Assert.Contains("have the same name", Rejected(Autograb("Bookmarks") + Autograb("bookmarks")));
    }

    [Theory]
    [InlineData("Link")]
    [InlineData("Clean")]
    [InlineData("Download")]
    [InlineData("download")]
    [InlineData("Audiobookshelf")]
    [InlineData("Manual search")]
    public void Load_BlockNamedLikeABuiltInJob_IsRejected(string name)
    {
        var message = Rejected(Autograb(name));

        Assert.Contains("is the name of a built-in job; choose another name", message);
    }

    [Theory]
    [InlineData("autograb[0]")]
    [InlineData("autograb[1]")]
    [InlineData("snatchlist[3]")]
    [InlineData("Hardcover_List[0]")]
    public void Load_NameOfTheGeneratedForm_IsRejectedEvenWithoutAClash(string name)
    {
        var message = Rejected(Autograb(name));

        Assert.Contains("are given to blocks without a name", message);
    }

    [Theory]
    [InlineData(" Bookmarks")]
    [InlineData("Bookmarks ")]
    public void Load_NameWithLeadingOrTrailingSpace_IsRejected(string name)
    {
        Assert.Contains("starts or ends with a space", Rejected(Autograb(name)));
    }

    [Fact]
    public void Load_UnnamedBlocksAndDistinctNames_Load()
    {
        var settings = Load(Autograb(null) + Autograb(null) + Autograb("Bookmarks") + Snatchlist("Unsat") + HardcoverList("Want to read"));

        Assert.Equal(3, settings.Autograb.Count);
    }

    private static string Library(string? name, string category) => name is null
        ? $"\n[[library]]\ncategory = \"{category}\"\nlibrary_dir = \"/library/{category}\"\n"
        : $"\n[[library]]\nname = \"{name}\"\ncategory = \"{category}\"\nlibrary_dir = \"/library/{category}\"\n";

    [Fact]
    public void Load_TwoLibrariesWithOneName_AreRejected()
    {
        var message = Rejected(Library("Books", "audiobooks") + Library("books", "ebooks"));

        Assert.Equal(
            "Invalid configuration: the [[library]] 'Books' and the [[library]] 'books' have the same name; every library needs its own.",
            message);
    }

    [Fact]
    public void Load_UnnamedLibraries_Load()
    {
        var settings = Load(Library(null, "audiobooks") + Library(null, "ebooks") + Library("Comics", "comics"));

        Assert.Equal(3, settings.Library.Count);
    }
}

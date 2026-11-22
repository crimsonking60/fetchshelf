using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// With `genres = true`: the Genre and Fiction filters of the Torrents and Books pages, and a torrent's main category and
/// genres on its page. With it off, none of it shows and the filters in an address are ignored.
/// </summary>
public sealed class GenreFilterPageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-genre-filter-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TorrentMetadata Book(string title, FictionCategory? fiction, params GenreCategory[] genres) =>
        OrganizerTestSupport.BuildMetadata(title) with { FictionCategory = fiction, GenreCategories = genres.ToHashSet() };

    private static readonly TorrentMetadata[] Books =
    [
        Book("Nonfiction History", FictionCategory.Nonfiction, GenreCategory.Historical, GenreCategory.Military),
        Book("Historical Novel", FictionCategory.Fiction, GenreCategory.Historical),
        Book("Space Romance", FictionCategory.Fiction, GenreCategory.ScienceFiction, GenreCategory.Romance),
        Book("Unsorted Book", null),
    ];

    private string Library => Path.Combine(_root, "library");

    private async Task<FetchshelfWebAppFixture> FixtureAsync(bool genres)
    {
        var fixture = new FetchshelfWebAppFixture($"""
            mam_id = ""
            genres = {(genres ? "true" : "false")}

            [[library]]
            name = "Ebooks"
            category = "ebooks"
            library_dir = "{Library}"
            """);
        for (var i = 0; i < Books.Length; i++)
        {
            // A folder with a file, so the Books page's catalog has the book.
            var folder = Path.Combine(Library, "Brandon Sanderson", Books[i].Title);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "book.epub"), "epub");
            await OrganizerTestSupport.SeedTorrentAsync(fixture, $"hash{i}", Books[i], libraryPath: folder);
            using var scope = fixture.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            var entity = await dbContext.Torrents.SingleAsync(t => t.Id == $"hash{i}");
            entity.ChosenEbookExtension = "epub";
            entity.LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new("book.epub", 4) });
            await dbContext.SaveChangesAsync();
        }

        return fixture;
    }

    private static string Rows(string html) => html[(html.IndexOf("<tbody", StringComparison.Ordinal) is var start and >= 0 ? start : 0)..];

    [Fact]
    public async Task TorrentsPage_GenreFilter_ByMamsName_AndNoGenre()
    {
        using var fixture = await FixtureAsync(genres: true);
        var client = fixture.CreateClient();

        var page = await client.GetStringAsync("/torrents");
        var historical = Rows(await client.GetStringAsync("/torrents?Genre=17"));
        var military = Rows(await client.GetStringAsync("/torrents?Genre=63"));
        var none = Rows(await client.GetStringAsync("/torrents?Genre=none"));

        Assert.Contains("<option value=\"63\">Military/War</option>", page);
        Assert.Contains("<option value=\"none\">No genre</option>", page);
        // A book with neither main category is tracked, so Neither is offered; the Fiction filter comes first.
        Assert.Contains("<option value=\"Neither\">Neither</option>", page);
        Assert.True(page.IndexOf("name=\"Fiction\"", StringComparison.Ordinal) < page.IndexOf("name=\"Genre\"", StringComparison.Ordinal));
        Assert.Contains("Nonfiction History", historical);
        Assert.Contains("Historical Novel", historical);
        Assert.DoesNotContain("Space Romance", historical);
        Assert.Contains("Nonfiction History", military);
        Assert.DoesNotContain("Historical Novel", military);
        Assert.Contains("Unsorted Book", none);
        Assert.DoesNotContain("Historical Novel", none);
    }

    [Fact]
    public async Task TorrentsPage_FictionFilter_WithNeither_CombinesWithTheGenre_AndShowsAsChips()
    {
        using var fixture = await FixtureAsync(genres: true);
        var client = fixture.CreateClient();

        var fiction = await client.GetStringAsync("/torrents?Fiction=Fiction&Genre=17");
        var neither = Rows(await client.GetStringAsync("/torrents?Fiction=Neither"));

        Assert.Contains("Historical Novel", Rows(fiction));
        Assert.DoesNotContain("Nonfiction History", Rows(fiction));
        Assert.DoesNotContain("Space Romance", Rows(fiction));
        Assert.Contains("Genre: Historical", fiction);
        Assert.Contains("Fiction: Fiction", fiction);
        // Each chip drops its own filter and keeps the other; the export keeps both.
        Assert.Contains("href=\"/Torrents?Fiction=Fiction&amp;Sort=CreatedAt&amp;Dir=desc&amp;PageSize=25\">&times;", fiction);
        Assert.Contains("href=\"/Torrents?Genre=17&amp;Sort=CreatedAt&amp;Dir=desc&amp;PageSize=25\">&times;", fiction);
        Assert.Contains("Genre=17&amp;Fiction=Fiction&amp;Sort=CreatedAt&amp;Dir=desc&amp;PageSize=25&amp;format=csv", fiction);
        Assert.Contains("Unsorted Book", neither);
        Assert.DoesNotContain("Historical Novel", neither);
    }

    [Fact]
    public async Task TorrentPage_ShowsTheMainCategoryAndGenres_ByMamsNames_LinkingToTheFilters()
    {
        using var fixture = await FixtureAsync(genres: true);

        var page = await fixture.CreateClient().GetStringAsync("/torrents/hash0");
        var unsorted = await fixture.CreateClient().GetStringAsync("/torrents/hash3");

        Assert.Contains("<a href=\"/Torrents?Fiction=Nonfiction\">Nonfiction</a>", page);
        Assert.Contains("<a href=\"/Torrents?Genre=17\">Historical</a>, <a href=\"/Torrents?Genre=63\">Military/War</a>", page);
        Assert.Matches("id=\"main-category\"><span class=\"label\">Main category</span><span class=\"value\">\\s*-</span>", unsorted);
    }

    [Fact]
    public async Task BooksPage_GenreAndFictionFilters_KeptByTheViews()
    {
        using var fixture = await FixtureAsync(genres: true);
        var client = fixture.CreateClient();

        var historical = await client.GetStringAsync("/books?Genre=17");
        var nonfiction = await client.GetStringAsync("/books?Genre=17&Fiction=Nonfiction");

        Assert.Contains("Nonfiction History", historical);
        Assert.Contains("Historical Novel", historical);
        Assert.DoesNotContain("Space Romance", historical);
        Assert.Contains("Nonfiction History", nonfiction);
        Assert.DoesNotContain("Historical Novel", nonfiction);
        Assert.Contains("<option value=\"63\">Military/War</option>", historical);
        // The Authors view keeps both filters.
        Assert.Contains("href=\"/Books?PageSize=25&amp;Genre=17&amp;Fiction=Nonfiction&amp;View=authors\"", nonfiction);
    }

    [Fact]
    public async Task BooksPage_TheFiltersOnTheirOwnLine_KeepWhatIsShown_AndClearFiltersKeepsTheSearchAndView()
    {
        using var fixture = await FixtureAsync(genres: true);
        var client = fixture.CreateClient();

        var authors = await client.GetStringAsync("/books?View=authors&Format=ebooks&Fiction=Fiction");
        var plain = await client.GetStringAsync("/books?View=authors");
        var search = await client.GetStringAsync("/books?Q=history&Genre=17");

        var filters = authors[authors.IndexOf("id=\"books-filters\"", StringComparison.Ordinal)..authors.IndexOf("</form>", StringComparison.Ordinal)];
        Assert.Contains("name=\"Format\"", filters);
        Assert.Contains("name=\"Fiction\"", filters);
        Assert.Contains("name=\"Genre\"", filters);
        Assert.DoesNotContain("name=\"Q\"", filters);
        // Changing a filter in the Authors view stays in it.
        Assert.Contains("<input type=\"hidden\" name=\"View\" value=\"authors\" />", filters);
        Assert.Contains("id=\"clear-filters\" href=\"/Books?View=authors&amp;PageSize=25\"", authors);
        Assert.Contains("id=\"clear-filters\" href=\"/Books?Q=history&amp;PageSize=25\"", search);
        Assert.DoesNotContain("id=\"clear-filters\"", plain);
    }

    [Fact]
    public async Task GenresOff_NoFilterOrGenreLine_AndTheFiltersInTheAddressAreIgnored()
    {
        using var fixture = await FixtureAsync(genres: false);
        var client = fixture.CreateClient();

        var torrents = await client.GetStringAsync("/torrents?Genre=17");
        var books = await client.GetStringAsync("/books?Fiction=Nonfiction");
        var torrent = await client.GetStringAsync("/torrents/hash0");

        Assert.DoesNotContain("name=\"Genre\"", torrents);
        Assert.DoesNotContain("name=\"Fiction\"", books);
        Assert.Contains("Space Romance", Rows(torrents));
        Assert.Contains("Historical Novel", books);
        Assert.DoesNotContain("id=\"genres\"", torrent);
        Assert.DoesNotContain("Main category", torrent);
    }

    [Fact]
    public void FictionChoices_Neither_OnlyWhenABookHasNeither_OrItIsInUse()
    {
        Assert.Equal(["Fiction", "Nonfiction"], Pages.GenreFilters.FictionChoices(false, null).Select(o => o.Value));
        Assert.Equal(["Fiction", "Nonfiction", "Neither"], Pages.GenreFilters.FictionChoices(true, null).Select(o => o.Value));
        Assert.Equal(["Fiction", "Nonfiction", "Neither"], Pages.GenreFilters.FictionChoices(false, "Neither").Select(o => o.Value));
    }
}

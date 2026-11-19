using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The torrent pages of a collection linked as its books, and of one of its books.</summary>
public class CollectionPagesTests
{
    private const string Hash = "939c95997a312c59cd9a2e2a89fb47aab0db5848";

    private static async Task<FetchshelfWebAppFixture> FixtureWithCollectionAsync()
    {
        var fixture = new FetchshelfWebAppFixture();
        await fixture.CreateClient().GetAsync("/Jobs"); // starts the host, which creates the database
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, OrganizerTestSupport.BuildMetadata("Discworld"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, CollectionBookId.For(Hash, 1), OrganizerTestSupport.BuildMetadata("Mort"), libraryPath: "/library/Mort");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, CollectionBookId.For(Hash, 2), OrganizerTestSupport.BuildMetadata("Thud!"), libraryPath: "/library/Thud");
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        foreach (var torrent in dbContext.Torrents)
        {
            if (torrent.Id == Hash)
            {
                torrent.CollectionBookCount = 2;
            }
            else
            {
                torrent.CollectionTorrentId = Hash;
            }
        }

        await dbContext.SaveChangesAsync();
        return fixture;
    }

    [Fact]
    public async Task CollectionPage_ListsItsBooks_WithoutCleanOrUnlink()
    {
        using var fixture = await FixtureWithCollectionAsync();

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/torrents/{Hash}"));

        Assert.Contains("A collection, linked as its 2 books", html);
        Assert.Contains($"<a href=\"/torrents/{Hash}-1\">Mort</a>", html);
        Assert.Contains($"<a href=\"/torrents/{Hash}-2\">Thud!</a>", html);
        Assert.DoesNotContain(">Unlink</button>", html);
        Assert.DoesNotContain(">Clean</button>", html);
        Assert.Contains(">Start</button>", html);
    }

    [Fact]
    public async Task BookPage_LinksItsCollection_AndLeavesTheTorrentsActionsToIt()
    {
        using var fixture = await FixtureWithCollectionAsync();

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/torrents/{Hash}-1"));

        Assert.Contains($"A book of the collection <a href=\"/torrents/{Hash}\">Discworld</a>", html);
        Assert.Contains(">Unlink</button>", html);
        Assert.DoesNotContain(">Start</button>", html);
        Assert.DoesNotContain("Remove from qBittorrent with data", html);
    }

    private static FetchshelfWebAppFixture SplittingFixture(bool librarySplits) => new($"""
        mam_id = ""
        split_collections = true

        [[library]]
        name = "Ebooks"
        category = "ebooks"
        library_dir = "/library/ebooks"
        split_collections = {(librarySplits ? "true" : "false")}
        """);

    private static async Task SeedEbookAsync(FetchshelfWebAppFixture fixture)
    {
        await fixture.CreateClient().GetAsync("/Jobs"); // starts the host, which creates the database
        var metadata = OrganizerTestSupport.BuildMetadata("Discworld") with { MediaType = Fetchshelf.Core.Metadata.MediaType.Ebook };
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, metadata, libraryPath: "/library/ebooks/Discworld");
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.Torrents.Single().LinkerName = "Ebooks";
        await dbContext.SaveChangesAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TorrentPage_OffersSplitting_StartingAsItsLibrary(bool librarySplits)
    {
        using var fixture = SplittingFixture(librarySplits);
        await SeedEbookAsync(fixture);

        var html = await fixture.CreateClient().GetStringAsync($"/torrents/{Hash}");

        Assert.Contains("name=\"splitShown\"", html);
        Assert.Equal(librarySplits, html.Contains("name=\"split\" value=\"true\" checked=\"checked\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TorrentPage_WithoutTheTopLevelSwitch_OffersNoSplitting()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedEbookAsync(fixture);

        Assert.DoesNotContain("splitShown", await fixture.CreateClient().GetStringAsync($"/torrents/{Hash}"));
    }

    [Theory]
    [InlineData(false, false)] // unticked in a library that splits: kept as this torrent's own choice
    [InlineData(true, null)] // ticked, as its library does: follows the library
    public async Task RefreshAndRelink_StoresTheChoice_OnlyWhenItDiffersFromTheLibrary(bool ticked, bool? stored)
    {
        using var fixture = SplittingFixture(librarySplits: true);
        await SeedEbookAsync(fixture);
        var client = fixture.CreateClient();
        var html = await client.GetStringAsync($"/torrents/{Hash}");
        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        var form = new List<KeyValuePair<string, string>> { new("splitShown", "true"), new("__RequestVerificationToken", token) };
        if (ticked)
        {
            form.Add(new("split", "true"));
        }

        await client.PostAsync($"/torrents/{Hash}?handler=RefreshAndRelink", new FormUrlEncodedContent(form));

        using var scope = fixture.CreateScope();
        Assert.Equal(stored, scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents.Single().SplitCollection);
    }

    [Fact]
    public async Task TorrentsList_SaysWhichRowsAreCollectionsAndTheirBooks()
    {
        using var fixture = await FixtureWithCollectionAsync();

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/Torrents"));

        Assert.Contains("(collection of 2 books)", html);
        Assert.Contains($"(from <a href=\"/torrents/{Hash}\">a collection</a>)", html);
    }
}

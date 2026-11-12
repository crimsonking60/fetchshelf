using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Series and Following pages, and the follow buttons on a torrent's page.</summary>
public partial class FollowPagesTests
{
    private const string ConfigWithFollow = """
        mam_id = ""

        [follow]
        category = "audiobooks"

        [[follow.grab]]
        cost = "free"
        """;

    private static TorrentMetadata Book(string title, string series, string number, string author = "James S. A. Corey") =>
        OrganizerTestSupport.BuildMetadata(title, [author]) with { Series = [SeriesParser.Parse(series, number)] };

    private static async Task SeedExpanseAsync(FetchshelfWebAppFixture fixture)
    {
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", Book("Leviathan Wakes", "The Expanse", "1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", Book("Caliban's War", "The Expanse", "2"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash4", Book("Cibola Burn", "The Expanse", "4"));
    }

    private static async Task<int> SeedFindAsync(
        FetchshelfWebAppFixture fixture, FollowFindStatus status = FollowFindStatus.New, FollowKind kind = FollowKind.Author, FollowMode mode = FollowMode.Watch)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var follow = new FollowEntity
        {
            Kind = kind,
            NameKey = "jamessacorey",
            DisplayName = "James S. A. Corey",
            Mode = mode,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Follows.Add(follow);
        dbContext.SaveChanges();
        var find = new FollowFindEntity
        {
            FollowId = follow.Id,
            MamId = 1,
            Title = "The Way of Kings",
            MediaSide = "Audio",
            Status = status,
            FoundAt = DateTimeOffset.UtcNow,
            StatusAt = DateTimeOffset.UtcNow,
        };
        dbContext.FollowFinds.Add(find);
        dbContext.SaveChanges();
        return find.Id;
    }

    private static FollowEntity? FollowOf(FetchshelfWebAppFixture fixture, FollowKind kind, string key)
    {
        using var scope = fixture.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Follows.AsNoTracking().SingleOrDefault(f => f.Kind == kind && f.NameKey == key);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pageUrl, string handlerUrl, Dictionary<string, string> fields, bool htmx = false)
    {
        var html = await client.GetStringAsync(pageUrl);
        fields["__RequestVerificationToken"] = TokenPattern().Match(html).Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, handlerUrl) { Content = new FormUrlEncodedContent(fields) };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task FollowingPage_ChangeToAuto_ReloadsThePageWithTheNewModeInTheHeading()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture, mode: FollowMode.Watch);
        using var client = fixture.CreateClient();

        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/Following"));
        var response = await PostAsync(client, "/Following", "/Following?handler=Mode",
            new() { ["kind"] = "Author", ["key"] = "jamessacorey", ["name"] = "James S. A. Corey", ["mode"] = "Download" });
        // The client follows the redirect, so the response is the page as it reloads.
        var after = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("/Following", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Author, Followed - download yourself</span>", before);
        Assert.Contains(">Change to Auto</button>", before);
        Assert.Contains("Author, Auto - downloaded automatically</span>", after);
        Assert.Contains(">Change to Followed</button>", after);
        Assert.DoesNotContain("Followed - download yourself</span>", after);
    }

    [Fact]
    public async Task FollowingPage_SaysWhenAFollowWasSearched_AndWhenItIsSearchedAgain()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow.Replace("[follow]", "[follow]\nsearch_again_after = 12", StringComparison.Ordinal));
        await SeedFindAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Follows.Single().LastSearchedAt = new DateTimeOffset(2026, 11, 12, 7, 57, 56, TimeSpan.Zero);
            dbContext.SaveChanges();
        }

        var html = await fixture.CreateClient().GetStringAsync("/Following");

        Assert.Matches(
            "Last searched <time class=\"local-time\" datetime=\"2026-11-12T07:57:56Z\"[^>]*>2026-11-12 07:57:56 UTC</time>, "
            + "will search again after <time class=\"local-time\" datetime=\"2026-11-12T19:57:56Z\"[^>]*>2026-11-12 19:57:56 UTC</time>\\.",
            html);
    }

    [Fact]
    public async Task FollowingPage_SaysWhatIsDownloadedAutomatically()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);

        var html = WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/Following"));

        Assert.Contains("<section id=\"follow-rules\"", html);
        Assert.Matches(
            "<p>For a series or author on Auto, this decides what is downloaded:</p>\\s*<ul>\\s*"
            + "<li>Ebooks and audiobooks: only free ones \\(freeleech, VIP or personal freeleech\\)\\.</li>\\s*</ul>",
            html);
        Assert.Contains("listed as <em>Not free</em>", html);
        Assert.Contains("A followed series or author only has what is found listed.", html);
        Assert.DoesNotContain("dry_run</code> is on", html);
    }

    [Fact]
    public async Task FollowingPage_SeveralGrabBlocks_AreListedInOrder_AndADryRunSaysNothingIsDownloaded()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithFollow.Replace("[follow]", "[follow]\ndry_run = true", StringComparison.Ordinal) + "\n\n[[follow.grab]]\ncost = \"wedge\"\n");

        var html = WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/Following"));

        Assert.Matches(
            "<ol>\\s*<li>Ebooks and audiobooks: only free ones \\(freeleech, VIP or personal freeleech\\)\\.</li>\\s*"
            + "<li>Ebooks and audiobooks: free ones, and others with a freeleech wedge\\.</li>\\s*</ol>",
            html);
        Assert.Contains("<code>dry_run</code> is on", html);
    }

    [Fact]
    public async Task FollowingPage_WithoutAFollowSection_HasNoRules()
    {
        using var fixture = new FetchshelfWebAppFixture();

        Assert.DoesNotContain("follow-rules", await fixture.CreateClient().GetStringAsync("/Following"));
    }

    [Fact]
    public async Task FollowingPage_AFindsTitle_OpensItsDetailsInADialog_AsOnTheSearchPage()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);

        var html = await fixture.CreateClient().GetStringAsync("/Following");

        Assert.Contains(
            "<a hx-get=\"/search/1?handler=Partial\" hx-target=\"#hit-dialog-body\" hx-swap=\"innerHTML\" href=\"/search/1\">The Way of Kings</a>",
            html);
        Assert.Contains("<dialog id=\"hit-dialog\"", html);
        Assert.Contains("id=\"hit-dialog-body\"", html);
    }

    [Fact]
    public async Task FollowingPage_ShowsEachFindsSize_AndNothingForAFindWithoutOne()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        var findId = await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();
        var withoutSize = await client.GetStringAsync("/Following");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.FollowFinds.Single(f => f.Id == findId).SizeBytes = 1288490189; // 1.2 GiB
            dbContext.SaveChanges();
        }

        var withSize = await client.GetStringAsync("/Following");

        Assert.Contains("<th>Size</th>", withoutSize);
        Assert.Contains("<td class=\"nowrap\"></td>", withoutSize);
        Assert.Contains("<td class=\"nowrap\">1.2 GiB</td>", withSize);
    }

    [Fact]
    public async Task SeriesPage_ShowsWhatIsOwnedAndMissing_WithoutFollowButtonsWithoutAFollowSection()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Series"));

        Assert.Contains(">The Expanse</a>", html);
        Assert.Contains("<td>1, 2, 4</td>", html);
        Assert.Contains("<td>3</td>", html);
        Assert.DoesNotContain("handler=Mode", html);
        Assert.DoesNotContain(">Following</a>", html);
    }

    [Fact]
    public async Task SeriesPage_Auto_SwapsTheRow_AndTheFilterFindsIt()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateClient();

        var response = await PostAsync(client, "/Series", "/Series?handler=Mode",
            new() { ["key"] = "expanse", ["name"] = "The Expanse", ["mode"] = "Download" }, htmx: true);
        var row = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.StartsWith("<tr id=\"series-", row.Trim());
        Assert.Contains(">Auto</span>", row);
        Assert.Contains(">Follow</button>", row);
        Assert.DoesNotContain(">Auto</button>", row); // the mode in use has no button
        Assert.Equal(FollowMode.Download, FollowOf(fixture, FollowKind.Series, "expanse")!.Mode);
        Assert.Contains("The Expanse", await client.GetStringAsync("/Series?Show=followed"));
        Assert.Contains(">Following</a>", await client.GetStringAsync("/Series"));
    }

    [Fact]
    public async Task SeriesPage_Ignore_LeavesTheSeriesOutOfThoseWithMissingBooks_AndStopUndoesIt()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateClient();
        Assert.Contains("The Expanse", await client.GetStringAsync("/Series?Show=missing"));

        await PostAsync(client, "/Series", "/Series?handler=Mode", new() { ["key"] = "expanse", ["name"] = "The Expanse", ["mode"] = "Ignore" }, htmx: true);

        Assert.DoesNotContain("The Expanse", await client.GetStringAsync("/Series?Show=missing"));
        Assert.Contains("The Expanse", await client.GetStringAsync("/Series?Show=ignored"));

        await PostAsync(client, "/Series", "/Series?handler=Mode", new() { ["key"] = "expanse", ["name"] = "The Expanse", ["mode"] = "Stop" }, htmx: true);

        Assert.Null(FollowOf(fixture, FollowKind.Series, "expanse"));
    }

    [Fact]
    public async Task TorrentPage_FollowAnAuthor_ComesBack_AndShowsItFollowed()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateNoRedirectClient();

        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/torrents/hash1"));
        Assert.Contains(">Follow The Expanse</button>", before);
        Assert.Contains(">Follow James S. A. Corey</button>", before);

        var response = await PostAsync(client, "/torrents/hash1", "/Following?handler=Follow", new()
        {
            ["kind"] = "Author",
            ["key"] = "jamessacorey",
            ["name"] = "James S. A. Corey",
            ["returnUrl"] = "/torrents/hash1",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/torrents/hash1", response.Headers.Location!.ToString());
        Assert.Equal(FollowMode.Watch, FollowOf(fixture, FollowKind.Author, "jamessacorey")!.Mode);
        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/torrents/hash1"));
        Assert.Contains(">Following James S. A. Corey</a>", after);
        // No message is left behind for a later visit to the Following page.
        Assert.DoesNotContain("id=\"follow-message\"", await client.GetStringAsync("/Following"));
    }

    [Fact]
    public async Task SeriesPage_Follow_ListsOnly_AndTheFilterNamesBothModes()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Series"));

        var response = await PostAsync(client, "/Series", "/Series?handler=Mode",
            new() { ["key"] = "expanse", ["name"] = "The Expanse", ["mode"] = "Watch" }, htmx: true);
        var row = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(page.IndexOf(">Follow</button>", StringComparison.Ordinal) < page.IndexOf(">Auto</button>", StringComparison.Ordinal), "Follow comes before Auto");
        Assert.Contains(">Auto</button>", page);
        Assert.Contains(">Auto or followed</option>", page);
        Assert.Contains(">Followed</span>", row);
        Assert.Equal(FollowMode.Watch, FollowOf(fixture, FollowKind.Series, "expanse")!.Mode);
    }

    [Fact]
    public async Task TorrentPage_FollowASeries_StartsItListingOnly_LikeAnAuthor()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedExpanseAsync(fixture);
        using var client = fixture.CreateNoRedirectClient();

        await PostAsync(client, "/torrents/hash1", "/Following?handler=Follow", new()
        {
            ["kind"] = "Series",
            ["key"] = "expanse",
            ["name"] = "The Expanse",
            ["returnUrl"] = "/torrents/hash1",
        });

        Assert.Equal(FollowMode.Watch, FollowOf(fixture, FollowKind.Series, "expanse")!.Mode);
    }

    [Fact]
    public async Task FollowingPage_AnIgnoredSeriesFinds_AreNotCountedInTheMenu()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedFindAsync(fixture, kind: FollowKind.Series, mode: FollowMode.Ignore);
        using var client = fixture.CreateClient();

        Assert.DoesNotContain("id=\"follow-badge\"", await client.GetStringAsync("/Following"));
    }

    [Fact]
    public async Task FollowingPage_Stop_RemovesTheFollowAndItsError()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedFindAsync(fixture);
        int followId;
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            followId = dbContext.Follows.Single().Id;
            await ErrorLog.RecordAsync(dbContext, ErrorStage.Follow, ErrorLog.FollowKey(followId), "James S. A. Corey", "boom");
            await dbContext.SaveChangesAsync();
        }

        using var client = fixture.CreateClient();
        await PostAsync(client, "/Following", "/Following?handler=Stop",
            new() { ["followId"] = followId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["name"] = "James S. A. Corey" });

        using var after = fixture.CreateScope();
        var context = after.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        Assert.Empty(context.Follows);
        Assert.Empty(context.FollowFinds);
        Assert.Empty(context.ErroredTorrents);
    }

    [Theory]
    [InlineData("Series", "expanse", "The Expanse", "7")]
    [InlineData("Series", "", "The Expanse", "Download")]
    [InlineData("Author", "jamessacorey", "James S. A. Corey", "Ignore")]
    public async Task FollowingPage_AModeChangeThatMakesNoSense_IsRefused(string kind, string key, string name, string mode)
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        using var client = fixture.CreateClient();

        var response = await PostAsync(client, "/Following", "/Following?handler=Mode",
            new() { ["kind"] = kind, ["key"] = key, ["name"] = name, ["mode"] = mode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Follows);
    }

    [Fact]
    public async Task SeriesPage_SearchFindsASeriesByItsAuthor()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedExpanseAsync(fixture);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash9", Book("Emma", "Austen Novels", "1", "Jane Austen"));
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Series?Q=corey");

        Assert.Contains("The Expanse", html);
        Assert.DoesNotContain("Austen Novels", html);
    }

    [Fact]
    public async Task FollowingPage_ListsFinds_AndCountsThemInTheMenu_AndDismissHidesOne()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        var findId = await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Following");
        Assert.Contains("The Way of Kings", html);
        Assert.Contains("id=\"follow-badge\"", html);

        await PostAsync(client, "/Following", "/Following?handler=Dismiss", new() { ["findId"] = findId.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        var after = await client.GetStringAsync("/Following");
        Assert.DoesNotContain("The Way of Kings", after);
        Assert.DoesNotContain("id=\"follow-badge\"", after);
    }

    [Fact]
    public async Task FollowingPage_DownloadAFind_SelectsIt_WithTheFollowsCategory()
    {
        const string SearchJson = """
            {
                "perpage": 100, "start": 0, "total": 1, "found": 1,
                "data": [{
                    "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "10 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dlhash1"
                }]
            }
            """;
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow, mamHandler: _ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchJson) });
        var findId = await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();

        var response = await PostAsync(client, "/Following", "/Following?handler=Download", new() { ["findId"] = findId.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Contains("Selected \"The Way of Kings\"", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var selected = Assert.Single(dbContext.SelectedTorrents);
        Assert.Equal(("audiobooks", "Follow: James S. A. Corey"), (selected.Category, selected.GrabberName));
        Assert.Equal(FollowFindStatus.Selected, dbContext.FollowFinds.Single().Status);
    }

    [Fact]
    public async Task FollowingPage_WithoutAFollowSection_SaysWhatIsNeeded()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        Assert.Contains("id=\"follow-not-configured\"", await client.GetStringAsync("/Following"));
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]*)""")]
    private static partial Regex TokenPattern();
}

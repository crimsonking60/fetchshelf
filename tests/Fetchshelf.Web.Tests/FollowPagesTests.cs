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

    /// <summary>Another find for the follow seeded by <see cref="SeedFindAsync"/>.</summary>
    private static int AddFind(
        FetchshelfWebAppFixture fixture, long mamId, string title, FollowFindStatus status = FollowFindStatus.New, string side = "Audio",
        string? seriesDisplay = null, long? size = null, DateTimeOffset? foundAt = null)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var find = new FollowFindEntity
        {
            FollowId = dbContext.Follows.First().Id,
            MamId = mamId,
            Title = title,
            MediaSide = side,
            SeriesDisplay = seriesDisplay,
            SizeBytes = size,
            Status = status,
            FoundAt = foundAt ?? DateTimeOffset.UtcNow,
            StatusAt = DateTimeOffset.UtcNow,
        };
        dbContext.FollowFinds.Add(find);
        dbContext.SaveChanges();
        return find.Id;
    }

    /// <summary>The seeded follow's own page.</summary>
    private static string FollowPage(FetchshelfWebAppFixture fixture) =>
        $"/following/{FollowOf(fixture, FollowKind.Author, "jamessacorey")!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

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
    public async Task FollowPage_ChangeToAuto_ReloadsThePageWithTheNewMode()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture, mode: FollowMode.Watch);
        using var client = fixture.CreateClient();
        var page = FollowPage(fixture);

        var before = WebUtility.HtmlDecode(await client.GetStringAsync(page));
        var response = await PostAsync(client, page, "/Following?handler=Mode",
            new() { ["kind"] = "Author", ["key"] = "jamessacorey", ["name"] = "James S. A. Corey", ["mode"] = "Download", ["returnUrl"] = page });
        // The client follows the redirect, so the response is the page as it reloads.
        var after = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(page, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Author, Followed - download yourself.", before);
        Assert.Contains(">Change to Auto</button>", before);
        Assert.Contains("Author, Auto - downloaded automatically.", after);
        Assert.Contains(">Change to Followed</button>", after);
        Assert.DoesNotContain("Followed - download yourself.", after);
    }

    [Fact]
    public async Task FollowingPage_ListsEachFollowOnARow_WithItsCounts_AndChangeToAutoComesBack()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);
        AddFind(fixture, 2, "Words of Radiance", FollowFindStatus.Wanted);
        AddFind(fixture, 3, "Oathbringer", FollowFindStatus.Owned);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Following"));
        var response = await PostAsync(client, "/Following", "/Following?handler=Mode",
            new() { ["kind"] = "Author", ["key"] = "jamessacorey", ["name"] = "James S. A. Corey", ["mode"] = "Download", ["returnUrl"] = "/Following?Q=corey" });
        var after = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains($"<a href=\"{FollowPage(fixture)}\">James S. A. Corey</a>", html);
        // To look at, not free, selected, in the library.
        Assert.Matches("<td>2</td>\\s*<td>1</td>\\s*<td></td>\\s*<td>1</td>", html);
        Assert.Contains(">To look at (2)</a>", html);
        Assert.DoesNotContain("The Way of Kings", html); // the finds are on the follow's own page
        Assert.Equal("/Following?Q=corey", response.RequestMessage!.RequestUri!.PathAndQuery);
        Assert.Contains("title=\"Downloaded automatically\">Auto</span>", after);
    }

    [Fact]
    public async Task FollowingPage_FiltersAndSearch_NarrowTheList_AndClearFiltersKeepsTheSearch()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Follows.Add(new FollowEntity
            {
                Kind = FollowKind.Series,
                NameKey = "expanse",
                DisplayName = "The Expanse",
                Mode = FollowMode.Download,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.SaveChanges();
        }

        using var client = fixture.CreateClient();
        var series = WebUtility.HtmlDecode(await client.GetStringAsync("/Following?Kind=Series&Q=exp"));
        var waiting = WebUtility.HtmlDecode(await client.GetStringAsync("/Following?Waiting=true"));
        var none = WebUtility.HtmlDecode(await client.GetStringAsync("/Following?Mode=Download&Q=corey"));

        Assert.Contains(">The Expanse</a>", series);
        Assert.DoesNotContain(">James S. A. Corey</a>", series);
        Assert.Contains("<a id=\"clear-filters\" href=\"/Following?Q=exp&Sort=Name&Dir=asc&PageSize=25\">", series);
        Assert.Contains(">James S. A. Corey</a>", waiting);
        Assert.DoesNotContain(">The Expanse</a>", waiting);
        Assert.Contains("id=\"no-matches-message\"", none);
    }

    [Fact]
    public async Task FollowingPage_Pages_TheFollows()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            for (var i = 0; i < 30; i++)
            {
                dbContext.Follows.Add(new FollowEntity
                {
                    Kind = FollowKind.Author,
                    NameKey = $"author{i:00}",
                    DisplayName = $"Author {i:00}",
                    Mode = FollowMode.Watch,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            dbContext.SaveChanges();
        }

        using var client = fixture.CreateClient();
        var first = WebUtility.HtmlDecode(await client.GetStringAsync("/Following"));
        var last = WebUtility.HtmlDecode(await client.GetStringAsync("/Following?PageNumber=2"));

        Assert.Contains("Showing 25 of 30. Page 1 of 2.", first);
        Assert.Contains(">Author 24</a>", first);
        Assert.DoesNotContain(">Author 25</a>", first);
        Assert.Contains(">Author 29</a>", last);
    }

    [Fact]
    public async Task FollowPage_SaysWhenAFollowWasSearched_AndWhenItIsSearchedAgain()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow.Replace("[follow]", "[follow]\nsearch_again_after = 12", StringComparison.Ordinal));
        await SeedFindAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Follows.Single().LastSearchedAt = new DateTimeOffset(2026, 11, 12, 7, 57, 56, TimeSpan.Zero);
            dbContext.SaveChanges();
        }

        var html = await fixture.CreateClient().GetStringAsync(FollowPage(fixture));

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
    public async Task ToLookAtPage_AFindsTitle_OpensItsDetailsInADialog_AsOnTheSearchPage()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);

        var html = await fixture.CreateClient().GetStringAsync("/following/to-look-at");

        Assert.Contains(
            "<a hx-get=\"/search/1?handler=Partial\" hx-target=\"#hit-dialog-body\" hx-swap=\"innerHTML\" href=\"/search/1\">The Way of Kings</a>",
            html);
        Assert.Contains("<dialog id=\"hit-dialog\"", html);
        Assert.Contains("id=\"hit-dialog-body\"", html);
    }

    [Fact]
    public async Task FollowPage_ShowsEachFindsSize_AndNothingForAFindWithoutOne()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        var findId = await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();
        var withoutSize = await client.GetStringAsync(FollowPage(fixture));
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.FollowFinds.Single(f => f.Id == findId).SizeBytes = 1288490189; // 1.2 GiB
            dbContext.SaveChanges();
        }

        var withSize = await client.GetStringAsync(FollowPage(fixture));

        Assert.Contains(">Size</a></th>", withoutSize);
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
        // A browser ends a <p> where a <form> starts, which spilled the buttons out of the details' grid.
        Assert.Contains("<div id=\"follow-buttons\">", before);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex("<p[ >](?:(?!</p>).)*<form", System.Text.RegularExpressions.RegexOptions.Singleline), before);
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
        Assert.Contains($"<a class=\"follow-state\" href=\"{FollowPage(fixture)}\">Following James S. A. Corey</a>", after);
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
    public async Task FollowingPage_AFindWaitingInAFormatNoGrabBlockTakes_IsNeitherListedNorCounted()
    {
        // The find is an audiobook, found before the only grab block was limited to ebooks.
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow + "\ncategories = { audio = false }\n");
        await SeedFindAsync(fixture);

        using var client = fixture.CreateClient();
        var html = await client.GetStringAsync("/Following");
        var followPage = await client.GetStringAsync(FollowPage(fixture));
        var toLookAt = await client.GetStringAsync("/following/to-look-at");

        Assert.Contains("James S. A. Corey", html);
        Assert.DoesNotContain("The Way of Kings", followPage);
        Assert.DoesNotContain("The Way of Kings", toLookAt);
        Assert.DoesNotContain("id=\"follow-badge\"", html);
    }

    [Fact]
    public async Task FollowingPage_SearchNow_WaitsForTheRun_SoThePageShowsTheNewSearchTime()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);
        var searchedAt = new DateTimeOffset(2026, 11, 12, 13, 20, 22, TimeSpan.Zero);
        fixture.JobRegistry.Register("Follow", async _ =>
        {
            await Task.Delay(200); // the search takes a moment; the page must wait for it
            using var scope = fixture.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Follows.Single().LastSearchedAt = searchedAt;
            dbContext.SaveChanges();
        });
        using var client = fixture.CreateClient();
        var followId = FollowOf(fixture, FollowKind.Author, "jamessacorey")!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var page = FollowPage(fixture);

        var response = await PostAsync(client, page, "/Following?handler=SearchNow",
            new() { ["followId"] = followId, ["name"] = "James S. A. Corey", ["returnUrl"] = page });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Searched MAM for James S. A. Corey.", html);
        Assert.Contains("Last searched <time class=\"local-time\" datetime=\"2026-11-12T13:20:22Z\"", html);
    }

    [Fact]
    public async Task FollowingPage_SearchNow_ARunThatFails_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);
        fixture.JobRegistry.Register("Follow", _ => throw new InvalidOperationException("MAM answered HTTP 500"));
        using var client = fixture.CreateClient();
        var followId = FollowOf(fixture, FollowKind.Author, "jamessacorey")!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var response = await PostAsync(client, "/Following", "/Following?handler=SearchNow", new() { ["followId"] = followId, ["name"] = "James S. A. Corey" });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("The search for James S. A. Corey failed: MAM answered HTTP 500", html);
    }

    [Fact]
    public async Task FollowPage_ASelectedFind_ShowsTheSearchPagesBadge_WhichAsksAgain()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture, FollowFindStatus.Selected);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
            {
                MamId = 1,
                DownloadLink = "dl1",
                CostDecision = "Ratio",
                MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata()),
                NormalizedTitleKey = "the way of kings",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.SaveChanges();
        }

        using var client = fixture.CreateClient();
        var html = await client.GetStringAsync(FollowPage(fixture) + "?Show=Selected");
        // A find downloaded from the list stays on what is to look at until it is linked, as on the Search page.
        var toLookAt = await client.GetStringAsync(FollowPage(fixture));

        // The badge asks the Search page's status check again, which moves it on and starts the Link job when done.
        Assert.Contains("hx-get=\"/?mamId=1&amp;round=1&amp;handler=Status\"", html);
        Assert.Contains("<span class=\"status-badge status-selected\">Selected</span>", html);
        Assert.Contains("hx-get=\"/?mamId=1&amp;round=1&amp;handler=Status\"", toLookAt);
        Assert.Contains("The Way of Kings", await client.GetStringAsync("/following/to-look-at"));
    }

    [Fact]
    public async Task FollowPage_AFindNowInTheLibrary_ShowsLinked_AndStopsAsking()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture, FollowFindStatus.Selected);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata() with { MamId = 1 }, libraryPath: "/library/x");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Torrents.Single().MamId = 1;
            dbContext.SaveChanges();
        }

        using var client = fixture.CreateClient();
        var html = await client.GetStringAsync(FollowPage(fixture) + "?Show=All");

        Assert.Contains("<span class=\"status-badge status-linked\">Linked</span>", html);
        Assert.DoesNotContain("handler=Status", html);
        // Linked, it has nothing left to look at, and counts as in the library before a search marks it so.
        var page = await client.GetStringAsync(FollowPage(fixture));
        Assert.DoesNotContain("The Way of Kings", page);
        Assert.Contains(">Selected: 0</a>", page);
        Assert.Contains(">In the library: 1</a>", page);
        Assert.Contains("The Way of Kings", await client.GetStringAsync(FollowPage(fixture) + "?Show=InLibrary"));
    }

    [Fact]
    public async Task ToLookAtPage_ANewFind_HasAnEmptyStatus_AndANotFreeOneSaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithFollow);
        await SeedFindAsync(fixture);

        var html = await fixture.CreateClient().GetStringAsync("/following/to-look-at");

        Assert.Matches("<td>\\s*</td>\\s*<td><local-time|<td>\\s*</td>\\s*<td><time", html);
        Assert.DoesNotContain(">New<", html);
        Assert.DoesNotContain("result-status", html);
        Assert.Equal("Not free", Fetchshelf.Web.Pages.Following.IndexModel.StatusLabel(FollowFindStatus.Wanted));
    }

    [Fact]
    public async Task ToLookAtPage_ListsFinds_AndCountsThemInTheMenu_AndDismissHidesOne()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        var findId = await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/following/to-look-at");
        Assert.Contains("The Way of Kings", html);
        Assert.Contains("id=\"follow-badge\"", html);
        Assert.Contains($">James S. A. Corey</a></td>", html); // whose find it is

        var response = await PostAsync(client, "/following/to-look-at", "/Following?handler=Dismiss",
            new() { ["findId"] = findId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["returnUrl"] = "/following/to-look-at" });

        var after = await response.Content.ReadAsStringAsync();
        Assert.Equal("/following/to-look-at", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("The Way of Kings", after);
        Assert.Contains("id=\"no-finds-message\"", after);
        Assert.DoesNotContain("id=\"follow-badge\"", after);
        Assert.Contains("The Way of Kings", await client.GetStringAsync(FollowPage(fixture) + "?Show=Dismissed"));
    }

    [Fact]
    public async Task FollowPage_ShowsWhatIsToLookAt_AndEachCountOpensItsFinds()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedFindAsync(fixture);
        AddFind(fixture, 2, "Oathbringer", FollowFindStatus.Owned);
        using var client = fixture.CreateClient();
        var page = FollowPage(fixture);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(page));
        var owned = WebUtility.HtmlDecode(await client.GetStringAsync(page + "?Show=InLibrary"));
        var all = WebUtility.HtmlDecode(await client.GetStringAsync(page + "?Show=All"));

        Assert.Contains("The Way of Kings", html);
        Assert.DoesNotContain("Oathbringer", html);
        Assert.Contains($"<a aria-current=\"true\" href=\"{page}?Sort=Found&Dir=desc&PageSize=25\">To look at: 1</a>", html);
        Assert.Contains($"<a href=\"{page}?Show=InLibrary&Sort=Found&Dir=desc&PageSize=25\">In the library: 1</a>", html);
        Assert.Contains("Oathbringer", owned);
        Assert.DoesNotContain("The Way of Kings", owned);
        Assert.Contains("Oathbringer", all);
        Assert.Contains("The Way of Kings", all);
    }

    [Fact]
    public async Task FollowPage_SortsAndFiltersTheFinds()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow + "\ncategories = { audio = true, ebook = true }\n");
        await SeedFindAsync(fixture, kind: FollowKind.Series);
        AddFind(fixture, 2, "Book Ten", seriesDisplay: "10", size: 100);
        AddFind(fixture, 3, "Book Two", side: "Ebook", seriesDisplay: "2", size: 300);
        using var client = fixture.CreateClient();
        var page = $"/following/{FollowOf(fixture, FollowKind.Series, "jamessacorey")!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        var byBook = await client.GetStringAsync(page + "?Sort=Book&Dir=asc");
        var bySize = await client.GetStringAsync(page + "?Sort=Size&Dir=desc");
        var ebooks = await client.GetStringAsync(page + "?Format=Ebook");

        Assert.True(byBook.IndexOf("Book Two", StringComparison.Ordinal) < byBook.IndexOf("Book Ten", StringComparison.Ordinal), "2 before 10");
        // A find without a size, or a number, comes last either way.
        Assert.True(byBook.IndexOf("Book Ten", StringComparison.Ordinal) < byBook.IndexOf("The Way of Kings", StringComparison.Ordinal));
        Assert.True(bySize.IndexOf("Book Two", StringComparison.Ordinal) < bySize.IndexOf("Book Ten", StringComparison.Ordinal), "largest first");
        Assert.True(bySize.IndexOf("Book Ten", StringComparison.Ordinal) < bySize.IndexOf("The Way of Kings", StringComparison.Ordinal));
        Assert.Contains("Book Two", ebooks);
        Assert.DoesNotContain("Book Ten", ebooks);
        Assert.Contains("id=\"clear-filters\"", ebooks);
    }

    [Fact]
    public async Task FollowPage_OfAnUnknownFollow_IsNotFound()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);

        var response = await fixture.CreateClient().GetAsync("/following/42");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_DismissesTheTickedFinds_AndComesBack()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        var first = await SeedFindAsync(fixture);
        var second = AddFind(fixture, 2, "Words of Radiance");
        AddFind(fixture, 3, "Oathbringer");
        using var client = fixture.CreateClient();
        var page = FollowPage(fixture);
        var html = await client.GetStringAsync(page);
        Assert.Contains($"<input class=\"find-select\" type=\"checkbox\" form=\"bulk-finds\" name=\"findIds\" value=\"{first}\"", html);

        var token = TokenPattern().Match(html).Groups[1].Value;
        using var content = new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", token),
            new("bulkAction", "dismiss"),
            new("findIds", first.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("findIds", second.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("returnUrl", page),
        ]);
        var response = await client.PostAsync("/Following?handler=Bulk", content);
        var after = await response.Content.ReadAsStringAsync();

        Assert.Equal(page, response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Dismissed 2 books.", after);
        Assert.Contains("Oathbringer", after);
        Assert.DoesNotContain("The Way of Kings", after);
        Assert.DoesNotContain("Words of Radiance", after);
    }

    [Fact]
    public async Task Bulk_WithNothingTicked_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow);
        await SeedFindAsync(fixture);
        using var client = fixture.CreateClient();

        var response = await PostAsync(client, "/following/to-look-at", "/Following?handler=Bulk",
            new() { ["bulkAction"] = "download", ["returnUrl"] = "/following/to-look-at" });

        Assert.Contains("Tick one or more books first.", await response.Content.ReadAsStringAsync());
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

        var response = await PostAsync(client, "/following/to-look-at", "/Following?handler=Download",
            new() { ["findId"] = findId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["returnUrl"] = "/following/to-look-at" });

        Assert.Contains("Selected \"The Way of Kings\"", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var selected = Assert.Single(dbContext.SelectedTorrents);
        Assert.Equal(("audiobooks", "Follow: James S. A. Corey"), (selected.Category, selected.GrabberName));
        Assert.Equal(FollowFindStatus.Selected, dbContext.FollowFinds.Single().Status);
    }

    [Fact]
    public async Task Bulk_DownloadsTheTickedFinds_AndSaysHowManyWereSelected()
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
        // MAM knows the first find's torrent only.
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithFollow, mamHandler: request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Regex.IsMatch(request.Content!.ReadAsStringAsync().Result, "\"id\":\\s*1\\b")
                    ? SearchJson
                    : """{"perpage": 100, "start": 0, "total": 0, "found": 0, "data": []}"""),
            });
        var first = await SeedFindAsync(fixture);
        var second = AddFind(fixture, 2, "Words of Radiance");
        using var client = fixture.CreateClient();
        var html = await client.GetStringAsync("/following/to-look-at");

        using var content = new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", TokenPattern().Match(html).Groups[1].Value),
            new("bulkAction", "download"),
            new("findIds", first.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("findIds", second.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("returnUrl", "/following/to-look-at"),
        ]);
        var response = await client.PostAsync("/Following?handler=Bulk", content);

        var message = Regex.Match(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), "id=\"follow-message\"[^>]*>([^<]*)").Groups[1].Value;
        Assert.Equal("Selected 1 of 2; they start at the next Download run. MAM returned no torrent with id 2.", message);
        using var scope = fixture.CreateScope();
        Assert.Equal(1, Assert.Single(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents).MamId);
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

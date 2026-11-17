using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public partial class AccountPageTests
{
    // No timers, so no job reaches MAM on its own while a test runs.
    private const string ConfigToml = """
        mam_id = "test-mam-id"
        search_interval = 0
        link_interval = 0
        unsat_buffer = 2

        [[autograb]]
        type = "bookmarks"
        name = "Careful"
        unsat_buffer = 5
        wedge_buffer = 30
        """;

    private static FetchshelfWebAppFixture Fixture(string accountJson, Action? onAccountRead = null, string configToml = ConfigToml) =>
        new(configToml, mamHandler: request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                onAccountRead?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(accountJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

    /// <summary>Starts the host and reads the account once, as a job run would.</summary>
    private static async Task<HttpClient> StartAndReadAccountAsync(FetchshelfWebAppFixture fixture)
    {
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MamClient>().GetAccountStateAsync();
        return client;
    }

    [Fact]
    public async Task AccountPage_AfterARead_ShowsTheNumbersAndWhatFetchshelfWorksOutFromThem()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);
        // A ratio-cost torrent started but not finished: MAM doesn't count it as downloaded yet.
        await AddSelectionAsync(fixture, mamId: 1, cost: "Ratio", sizeBytes: 3L * 1024 * 1024 * 1024, started: true);
        await AddSelectionAsync(fixture, mamId: 2, cost: "Vip", sizeBytes: 1024, started: false);

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("example-user, Power User", html);
        Assert.Matches("Last answered successfully <time class=\"local-time\"[^>]*>[^<]+</time>\\.", html);
        Assert.DoesNotContain("<local-time", html);
        Assert.Contains("1.2 TiB / 436.0 GiB", html);
        Assert.Contains("14 of 20", html);
        Assert.Contains("41,250", html);
        // uploaded / min_ratio 2 - downloaded - the 3 GiB in flight
        Assert.Contains("180.5 GiB", html);
        Assert.Contains("uploaded 1.2 TiB / min_ratio 2 - downloaded 436.0 GiB - 3.0 GiB started but not finished", html);
        // 20 allowed - 14 in use - 2 globally, or - 5 for the autograb with its own unsat_buffer.
        Assert.Contains("unsatisfied-snatch slots: 20 allowed - 14 in use - unsat_buffer 2", html);
        Assert.Contains("unsatisfied-snatch slots: 20 allowed - 14 in use - unsat_buffer 5", html);
        Assert.Contains("37 wedges - wedge_buffer 0", html);
        Assert.Contains("37 wedges - wedge_buffer 30", html);
        Assert.Contains("Autograb Careful", html);
        Assert.Matches(@"1 waiting on the <a href=""/Selected"">Selected</a> page; the numbers above would let the Download job start 1 of them", html);
        Assert.Contains("Connectable: yes", html);
    }

    [Fact]
    public async Task AccountPage_SnatchTable_UsesMamsLabelsWithSpacesCollapsedAndADashForNoSize()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("<td>Seeding - Uploads</td>", html);
        Assert.Matches(@"<td>Not Seeding - Uploads</td>\s*<td>0</td>\s*<td class=""nowrap"">-</td>", html);
        Assert.Matches(@"<td>Seeding - Satisfied</td>\s*<td>230</td>\s*<td class=""nowrap"">850.0 GiB</td>", html);
    }

    [Theory]
    [InlineData("inactHnr", "Not Seeding - H&amp;R - Not Yet Satisfied")]
    [InlineData("inactUnsat", "Not Seeding - Not Yet Satisfied")]
    [InlineData("ite", "Important Tracker Errors")]
    public async Task AccountPage_KindThatNeedsAttention_IsWarnedAboutFirst(string key, string label)
    {
        using var fixture = Fixture(MamAccountFixture.WithCounts((key, 2)));
        var client = await StartAndReadAccountAsync(fixture);

        var html = await client.GetStringAsync("/Account");

        var warning = html.IndexOf($"{label}: 2. See the <a href=\"https://www.myanonamouse.net/u/123456\"", StringComparison.Ordinal);
        Assert.True(warning >= 0);
        Assert.True(warning < html.IndexOf("<h3>From MAM</h3>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AccountPage_NothingNeedsAttention_ShowsNoWarning()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);

        Assert.DoesNotContain("snatch-warning", await client.GetStringAsync("/Account"));
    }

    [Fact]
    public async Task AccountPage_MamRefusedTheSession_ShowsThePauseAndWhatToDo()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml, mamHandler: _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        using (var scope = fixture.CreateScope())
        {
            await Assert.ThrowsAsync<MamForbiddenException>(() => scope.ServiceProvider.GetRequiredService<MamClient>().GetAccountStateAsync());
        }

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("id=\"mam-paused\"", html);
        Assert.Contains("MAM refused mam_id (HTTP 403).", html);
        Assert.Contains("fix mam_id in config.toml and restart Fetchshelf", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"no-account-read\"", html);
        Assert.Contains("Last refusal", html);
        Assert.Contains("MAM calls are paused until", await client.GetStringAsync("/Jobs")); // the strip says so too
    }

    [Fact]
    public async Task Refresh_GateFree_ReadsTheAccountAgain()
    {
        var reads = 0;
        using var fixture = Fixture(MamAccountFixture.Json, () => reads++);
        var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/Account");
        Assert.Contains("id=\"no-account-read\"", page);

        var html = await PostRefreshAsync(client, page);

        Assert.Equal(1, reads);
        Assert.Contains("14 of 20", html);
    }

    [Fact]
    public async Task Refresh_JobHoldingTheGate_SaysWhichAndKeepsTheLastNumbers()
    {
        var reads = 0;
        using var fixture = Fixture(MamAccountFixture.Json, () => reads++);
        var client = await StartAndReadAccountAsync(fixture);
        var page = await client.GetStringAsync("/Account");
        using var scope = fixture.CreateScope();
        using var held = await scope.ServiceProvider.GetRequiredService<MamAccessGate>().AcquireAsync("Autograb Freeleech");

        var html = await PostRefreshAsync(client, page);

        Assert.Contains("MAM is busy with <strong>Autograb Freeleech</strong>", html);
        Assert.Matches("The numbers below are from <time class=\"local-time\"[^>]*>[^<]+</time>\\.", html);
        Assert.Contains("14 of 20", html);
        Assert.Equal(1, reads); // only the read before the refresh
    }

    [Fact]
    public async Task AccountPage_WithoutMamId_HasNoNavigationLinkAndSaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var jobs = await client.GetStringAsync("/Jobs");
        var account = await client.GetStringAsync("/Account");

        Assert.DoesNotContain("href=\"/Account\"", jobs);
        Assert.DoesNotContain("id=\"account-strip\"", jobs);
        Assert.Contains("id=\"account-not-configured\"", account);
    }

    [Fact]
    public async Task JobsPage_WithAnAccountRead_ShowsTheStripLinkingToTheAccountPage()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);

        var html = await client.GetStringAsync("/Jobs");

        var strip = html[html.IndexOf("id=\"account-strip\"", StringComparison.Ordinal)..html.IndexOf("id=\"job-list\"", StringComparison.Ordinal)];
        Assert.Contains("Ratio 2.84", strip);
        Assert.Contains("Unsatisfied 14 of 20", strip);
        Assert.Contains("Wedges 37", strip);
        Assert.Contains("Ratio budget 183.5 GiB", strip);
        Assert.Contains("href=\"/Account\"", strip);
    }

    [Fact]
    public async Task AccountAndJobsPages_SelectionInFlightThatCantBeRead_StillShowAndSayWhy()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);
        await AddSelectionAsync(fixture, mamId: 1, cost: "Ratio", sizeBytes: 1, started: true, metadataJson: "{ not json");

        var jobs = await client.GetAsync("/Jobs");
        var account = await client.GetStringAsync("/Account");

        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
        Assert.Contains("Wedges 37", await jobs.Content.ReadAsStringAsync());
        Assert.Contains("id=\"derived-error\"", account);
        Assert.Contains("14 of 20", account); // what MAM said is still shown
    }

    [Fact]
    public async Task AccountPage_QueuedSelectionWithAnUnknownCost_IsLeftOutOfTheForecast()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);
        await AddSelectionAsync(fixture, mamId: 1, cost: "NoLongerACost", sizeBytes: 1, started: false);
        await AddSelectionAsync(fixture, mamId: 2, cost: "Vip", sizeBytes: 1, started: false);

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("2 waiting on the", html);
        Assert.Contains("would let the Download job start 1 of them", html);
    }

    [Fact]
    public async Task Refresh_AnswerThatIsNotTheAccount_SaysItCouldNotBeRead()
    {
        using var fixture = Fixture("<html>Please log in</html>");
        var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/Account");

        var html = await PostRefreshAsync(client, page);

        Assert.Contains("id=\"refresh-error\"", html);
        Assert.Contains("MAM&#x27;s answer could not be read", html);
    }

    [Fact]
    public async Task Refresh_RedirectsToThePage_SoReloadingDoesntAskMamAgain()
    {
        var reads = 0;
        using var fixture = Fixture(MamAccountFixture.Json, () => reads++);
        var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/Account");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups[1].Value,
        });

        var response = await client.PostAsync("/Account?handler=Refresh", form);
        await client.GetStringAsync(response.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account", response.Headers.Location.OriginalString);
        Assert.Equal(1, reads);
    }

    private static async Task AddSelectionAsync(
        FetchshelfWebAppFixture fixture, long mamId, string cost, long sizeBytes, bool started, string? metadataJson = null)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var metadata = OrganizerTestSupport.BuildMetadata() with { MamId = mamId, TotalSizeBytes = sizeBytes };
        dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = mamId,
            DownloadLink = $"dl{mamId}",
            CostDecision = cost,
            MetadataJson = metadataJson ?? Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = started ? DateTimeOffset.UtcNow : null,
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task AccountPage_NoBonusSection_SaysNothingIsSpentAndWhatThePointsWouldBuy()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("Fetchshelf spends no bonus points. The 41,250 points would buy 82 GB of upload credit.", html);
    }

    [Fact]
    public async Task AccountPage_BonusSection_ShowsWhatTheNextRunWouldBuyAndTheLastPurchases()
    {
        using var fixture = Fixture(MamAccountFixture.Json, configToml: ConfigToml + """


            [bonus]
            bonus_interval = 0
            keep_points = 30000
            upload_amounts = [20, 5]
            """);
        var client = await StartAndReadAccountAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Events.Add(new EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow.AddHours(-2),
                Kind = EventKind.BonusPurchase,
                BonusSpend = "Wedge",
                PointsBefore = 91_250,
                PointsAfter = 41_250,
            });
            await dbContext.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/Account");

        // 11,250 above keep_points: 20 GB (10,000), and the 1,250 left don't pay for 5 GB.
        Assert.Contains("would buy 20 GB of upload credit:", html);
        Assert.Contains("the 1,250 points above keep_points 30,000 don&#x27;t pay for 5 GB (2,500)", html);
        Assert.Contains("<td>a wedge</td>", html);
        Assert.Contains("91,250 → 41,250", html);
    }

    private static async Task<string> PostRefreshAsync(HttpClient client, string page)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups[1].Value,
        });
        var response = await client.PostAsync("/Account?handler=Refresh", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Waits for the recorder, which writes in the background, to have stored the day's snapshot.</summary>
    private static async Task<List<AccountSnapshotEntity>> SnapshotsAsync(FetchshelfWebAppFixture fixture, int atLeast = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var scope = fixture.CreateScope();
            var rows = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().AccountSnapshots.ToList();
            if (rows.Count >= atLeast || DateTime.UtcNow > deadline)
            {
                return rows;
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task AccountPage_AnAccountRead_IsKeptAsTheDaysSnapshot_AndShownOverTime()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);

        var snapshot = Assert.Single(await SnapshotsAsync(fixture));
        var html = await client.GetStringAsync("/Account");

        Assert.Equal(37, snapshot.Wedges);
        Assert.Contains("<h3>Over time</h3>", html);
        Assert.Contains("<svg class=\"sparkline\"", html);
        Assert.Contains("aria-label=\"Wedges over the last 90 days, from 37 to 37\"", html);
        Assert.Contains($"<td class=\"nowrap\">{snapshot.Day[..7]}</td>", html);
        Assert.Contains("href=\"/Account?handler=SnapshotsCsv\"", html);
    }

    [Fact]
    public async Task AccountPage_NoSnapshotYet_SaysWhenADayIsRecorded()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);
        await SnapshotsAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            db.AccountSnapshots.RemoveRange(db.AccountSnapshots);
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("Nothing recorded yet. A day is recorded once any job has read the account that day.", html);
        Assert.DoesNotContain("<svg class=\"sparkline\"", html);
    }

    [Fact]
    public async Task AccountPage_BeforeAnyReadSinceStartup_StillShowsTheHistory()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            db.AccountSnapshots.Add(new AccountSnapshotEntity { Day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), TakenAt = DateTimeOffset.UtcNow, Ratio = 3, Wedges = 4 });
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/Account");

        Assert.Contains("id=\"no-account-read\"", html);
        Assert.Contains("aria-label=\"Ratio over the last 90 days, from 3 to 3\"", html);
    }

    [Fact]
    public async Task SnapshotsCsv_DownloadsEveryDay()
    {
        using var fixture = Fixture(MamAccountFixture.Json);
        var client = await StartAndReadAccountAsync(fixture);
        var day = Assert.Single(await SnapshotsAsync(fixture)).Day;

        using var response = await client.GetAsync("/Account?handler=SnapshotsCsv");
        var lines = (await response.Content.ReadAsStringAsync()).TrimEnd('\n').Split('\n');

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("fetchshelf-account-snapshots.csv", response.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(day + ",", lines[1]);
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""")]
    private static partial Regex AntiforgeryToken();
}

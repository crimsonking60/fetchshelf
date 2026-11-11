using System.Text.RegularExpressions;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Job failures on the Errors and Jobs pages, and the badge on the navigation's Errors link.</summary>
public partial class JobFailuresPageTests
{
    private static async Task FailTwiceAsync(FetchshelfWebAppFixture fixture, string jobName, Func<Exception> error)
    {
        fixture.JobRegistry.Register(jobName, _ => throw error());
        await fixture.JobRegistry.TriggerAsync(jobName);
        await fixture.JobRegistry.TriggerAsync(jobName);
    }

    private static async Task AddErrorAsync(FetchshelfWebAppFixture fixture, ErrorStage stage, string key, string title, DateTimeOffset? at = null)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
        {
            Stage = stage,
            Key = key,
            Title = title,
            ErrorText = $"{title} failed",
            OccurredAt = at ?? DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ErrorsPage_FailingJob_ShowsItsErrorSinceWhenAndRunNow()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await FailTwiceAsync(fixture, "Link", () => new InvalidOperationException("the connection to qBittorrent failed"));
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Errors");

        var section = html[html.IndexOf("id=\"failing-jobs\"", StringComparison.Ordinal)..];
        Assert.Contains("href=\"/Jobs/History?job=Link\"", section);
        Assert.Contains("the connection to qBittorrent failed", section);
        Assert.Contains("failing since", section);
        Assert.Contains("(2 runs)", section);
        Assert.Contains(">Run now</button>", section);
    }

    [Fact]
    public async Task ErrorsPage_SeveralJobsRefusedByMam_AreOneLineNamingThem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        foreach (var job in new[] { "Autograb Freeleech", "Download", "Snatchlist Unsat", "Hardcover list" })
        {
            await FailTwiceAsync(fixture, job, () => new MamForbiddenException("MAM refused the request (HTTP 403)."));
        }

        await FailTwiceAsync(fixture, "Link", () => new InvalidOperationException("disk full"));
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Errors");

        Assert.Single(Regex.Matches(html, "MAM refused mam_id \\(HTTP 403\\)"));
        Assert.Contains("and 2 more", html);
        Assert.Contains("Fix mam_id in config.toml", html);
        Assert.Contains("disk full", html); // the unrelated failure keeps its own line
        Assert.Equal(2, Regex.Matches(html, "<li class=\"notice error\">").Count);
    }

    [Fact]
    public async Task ErrorsPage_JobThatSucceededAgain_IsNotListed()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var fail = true;
        fixture.JobRegistry.Register("Link", _ => fail ? throw new InvalidOperationException("boom") : Task.CompletedTask);
        await fixture.JobRegistry.TriggerAsync("Link");
        fail = false;
        await fixture.JobRegistry.TriggerAsync("Link");
        var client = fixture.CreateClient();

        Assert.DoesNotContain("id=\"failing-jobs\"", await client.GetStringAsync("/Errors"));
    }

    [Fact]
    public async Task ErrorsPage_NewStages_AreListedWithLinksToTheirTorrentOrList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Errors"); // starts the host, which creates the database
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1"); // MAM id 1
        await AddErrorAsync(fixture, ErrorStage.Snatchlist, "1", "Snatched Book");
        await AddErrorAsync(fixture, ErrorStage.HardcoverList, "hardcover:status:want_to_read 312460", "Dune");
        await AddErrorAsync(fixture, ErrorStage.Audiobookshelf, "hash1", "Linked Book");

        var html = await client.GetStringAsync("/Errors");

        Assert.Contains("<td class=\"nowrap\">Snatchlist</td>", html);
        Assert.Contains("<td class=\"nowrap\">Hardcover list</td>", html);
        Assert.Contains("<td class=\"nowrap\">Audiobookshelf</td>", html);
        Assert.Contains("<a href=\"/torrents/hash1\">Snatched Book</a>", html);
        Assert.Contains("<a href=\"/Lists/Detail/hardcover%3Astatus%3Awant_to_read\">Dune</a>", html);
        Assert.Contains("Hardcover id 312460", html);
        Assert.Contains("<a href=\"/torrents/hash1\">Linked Book</a>", html);
    }

    [Fact]
    public async Task ErrorsPage_StageFilter_ShowsOnlyThatJobsRows()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Errors");
        await AddErrorAsync(fixture, ErrorStage.HardcoverList, "list 1", "Dune");
        await AddErrorAsync(fixture, ErrorStage.Audiobookshelf, "hash1", "Linked Book");

        var html = await client.GetStringAsync("/Errors?Stage=HardcoverList");

        Assert.Contains("Dune", html);
        Assert.DoesNotContain("Linked Book", html);
        Assert.Contains("<option value=\"HardcoverList\" selected=\"selected\">Hardcover list</option>", html);
        Assert.Contains("<option value=\"Snatchlist\">Snatchlist</option>", html);
    }

    [Fact]
    public async Task JobsPage_FailingJob_SaysSinceWhen()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await FailTwiceAsync(fixture, "Link", () => new InvalidOperationException("boom"));
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");

        Assert.Contains("<tr class=\"failing\">", html);
        Assert.Matches(@"failing since <time[^>]*>[^<]*</time> \(2 runs\)", html);
    }

    [Fact]
    public async Task Badge_CountsFailingJobsAndRecentRowsButNotOldOnes()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await FailTwiceAsync(fixture, "Link", () => new InvalidOperationException("boom"));
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        await AddErrorAsync(fixture, ErrorStage.Linker, "hash1", "New");
        await AddErrorAsync(fixture, ErrorStage.Linker, "hash2", "Also new");
        await AddErrorAsync(fixture, ErrorStage.Linker, "hash3", "Seen a month ago", DateTimeOffset.UtcNow.AddDays(-30));

        var html = await client.GetStringAsync("/Torrents");

        Assert.Contains("<span id=\"error-badge\" class=\"nav-badge\" title=\"1 job failing, 2 new errors\">3</span>", html);
    }

    [Fact]
    public async Task Badge_NothingWrongOrOnlyOldRows_IsAbsent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        await AddErrorAsync(fixture, ErrorStage.Linker, "hash3", "Seen a month ago", DateTimeOffset.UtcNow.AddDays(-30));

        Assert.DoesNotContain("error-badge", await client.GetStringAsync("/Torrents"));
    }
}

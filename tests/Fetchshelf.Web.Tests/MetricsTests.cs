using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Entities;
using Fetchshelf.Tests.Shared;
using Fetchshelf.Web.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public class MetricsTests
{
    // No timers, so no job reaches MAM on its own while a test runs.
    private const string Config = """
        mam_id = "test-mam-id"
        search_interval = 0
        link_interval = 0
        metrics = true
        """;

    [Fact]
    public void Format_WritesHelpTypeAndSamples_EscapingLabels_AndLeavesOutMetricsWithoutSamples()
    {
        var text = PrometheusText.Format(
        [
            Metric.Gauge("fetchshelf_mam_ratio", "The ratio.", 2.5),
            new Metric("fetchshelf_torrents", "gauge", "Torrents,\nby library.",
                [new MetricSample(3, [("media_type", "Audiobook"), ("library", "My \"best\" \\ books\n")])]),
            new Metric("fetchshelf_unknown", "gauge", "Not known yet.", []),
            Metric.Gauge("fetchshelf_infinite", "Ratio with nothing downloaded.", double.PositiveInfinity),
        ]);

        Assert.Equal(
            "# HELP fetchshelf_mam_ratio The ratio.\n"
            + "# TYPE fetchshelf_mam_ratio gauge\n"
            + "fetchshelf_mam_ratio 2.5\n"
            + "# HELP fetchshelf_torrents Torrents,\\nby library.\n"
            + "# TYPE fetchshelf_torrents gauge\n"
            + "fetchshelf_torrents{media_type=\"Audiobook\",library=\"My \\\"best\\\" \\\\ books\\n\"} 3\n"
            + "# HELP fetchshelf_infinite Ratio with nothing downloaded.\n"
            + "# TYPE fetchshelf_infinite gauge\n"
            + "fetchshelf_infinite +Inf\n",
            text);
    }

    [Fact]
    public async Task Metrics_Off_IsNotFound()
    {
        using var fixture = new FetchshelfWebAppFixture(Config.Replace("metrics = true", "", StringComparison.Ordinal));

        var response = await fixture.CreateClient().GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ReportStoredValues_WithoutAskingMam()
    {
        var mamRequests = 0;
        using var fixture = new FetchshelfWebAppFixture(Config, mamHandler: request =>
        {
            mamRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("jsonLoad.php") ? MamAccountFixture.Json : "{}"),
            };
        });
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MamClient>().GetAccountStateAsync();
        }

        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: "/library/book");
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = "hash1",
            MamId = 1,
            Kind = EventKind.Grabbed,
            Cost = "Vip",
            WedgeUsed = false,
        });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity { OccurredAt = DateTimeOffset.UtcNow, TorrentId = "hash1", Kind = EventKind.Linked });
        fixture.JobRegistry.Register("test-job", _ => Task.FromResult<JobOutcome?>(null));
        await fixture.JobRegistry.TriggerAsync("test-job");
        var requestsBefore = mamRequests;

        var response = await client.GetAsync("/metrics");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/plain; version=0.0.4; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(requestsBefore, mamRequests);
        Assert.Contains("fetchshelf_mam_ratio 2.84\n", text);
        Assert.Contains("fetchshelf_mam_bonus_points 41250\n", text);
        Assert.Contains("fetchshelf_mam_unsatisfied 14\n", text);
        Assert.Contains("fetchshelf_mam_unsatisfied_limit 20\n", text);
        Assert.Contains("fetchshelf_ratio_budget_bytes ", text);
        Assert.Contains("fetchshelf_mam_paused 0\n", text);
        Assert.Contains("fetchshelf_selected_waiting 0\n", text);
        Assert.Contains("fetchshelf_torrents{media_type=\"Audiobook\",library=\"\"} 1\n", text);
        Assert.Contains("fetchshelf_grabbed_total{cost=\"vip\"} 1\n", text);
        Assert.Contains("fetchshelf_grabbed_total{cost=\"ratio\"} 0\n", text);
        Assert.Contains("fetchshelf_linked_total 1\n", text);
        Assert.Matches("fetchshelf_job_last_run_timestamp_seconds\\{job=\"test-job\",result=\"success\"\\} \\d+\n", text);
        Assert.Contains("fetchshelf_job_failing{job=\"test-job\"} 0\n", text);
    }

    [Fact]
    public async Task Metrics_BeforeTheAccountIsRead_LeaveTheAccountOut()
    {
        using var fixture = new FetchshelfWebAppFixture(Config);

        var text = await fixture.CreateClient().GetStringAsync("/metrics");

        Assert.DoesNotContain("fetchshelf_mam_ratio", text);
        Assert.DoesNotContain("fetchshelf_ratio_budget_bytes", text);
        Assert.Contains("fetchshelf_selected_waiting 0\n", text);
    }
}

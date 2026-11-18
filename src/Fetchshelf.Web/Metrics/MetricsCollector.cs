using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Statistics;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Metrics;

/// <summary>
/// What `/metrics` reports, from what is stored or cached: the last account read, the queue, the torrents, the events
/// and the job registry. A scrape never calls MAM or qBittorrent. Values not known yet, such as the account before the
/// first read, are left out rather than reported as 0.
/// </summary>
public sealed class MetricsCollector(
    FetchshelfDbContext dbContext, MamClient mamClient, IJobRegistry jobRegistry, FetchshelfSettings settings)
{
    /// <summary>The `cost` label of each grab table column.</summary>
    private static readonly Dictionary<string, string> CostLabels = new()
    {
        [LibraryStatistics.Free] = "freeleech",
        [LibraryStatistics.Vip] = "vip",
        [LibraryStatistics.PersonalFreeleech] = "personal_freeleech",
        [LibraryStatistics.Wedge] = "wedge",
        [LibraryStatistics.Ratio] = "ratio",
    };

    public async Task<IReadOnlyList<Metric>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new List<Metric>();
        if (mamClient.LastAccountState?.State is { } account)
        {
            metrics.Add(Metric.Gauge("fetchshelf_mam_ratio", "The account's ratio, as last read from MAM.", account.Ratio));
            metrics.Add(Metric.Gauge("fetchshelf_mam_uploaded_bytes", "The account's upload, as last read from MAM.", account.UploadedBytes));
            metrics.Add(Metric.Gauge("fetchshelf_mam_downloaded_bytes", "The account's download, as last read from MAM.", account.DownloadedBytes));
            metrics.Add(Metric.Gauge("fetchshelf_mam_wedges", "Freeleech wedges the account has, as last read from MAM.", account.Wedges));
            metrics.Add(Metric.Gauge("fetchshelf_mam_unsatisfied", "The account's unsatisfied snatches, as last read from MAM.", account.UnsatCount));
            metrics.Add(Metric.Gauge("fetchshelf_mam_unsatisfied_limit", "The account's unsatisfied-snatch limit, as last read from MAM.", account.UnsatLimit));
            if (account.BonusPoints is { } points)
            {
                metrics.Add(Metric.Gauge("fetchshelf_mam_bonus_points", "The account's bonus points, as last read from MAM.", points));
            }

            var inFlight = await SelectionQueue.InFlightBytesAsync(dbContext, cancellationToken);
            metrics.Add(Metric.Gauge(
                "fetchshelf_ratio_budget_bytes", "What may still be downloaded on ratio without going below min_ratio, as the Account page works it out.",
                DownloadBudget.ComputeRatioBudgetBytes(account.UploadedBytes, account.DownloadedBytes, inFlight, settings.MinRatio)));
        }

        metrics.Add(Metric.Gauge("fetchshelf_mam_paused", "1 while calls to MAM are paused after MAM refused one, else 0.", mamClient.PausedUntil is null ? 0 : 1));
        metrics.Add(Metric.Gauge(
            "fetchshelf_selected_waiting", "Selections waiting to be downloaded.", (await SelectionQueue.PendingAsync(dbContext, cancellationToken)).Count));

        var torrents = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.LibraryPath != null)
            .GroupBy(t => new { t.MediaType, t.LinkerName })
            .Select(g => new { g.Key.MediaType, g.Key.LinkerName, Count = g.Count() })
            .ToListAsync(cancellationToken);
        metrics.Add(new Metric("fetchshelf_torrents", "gauge", "Torrents in the libraries, by media type and library.",
            torrents.OrderBy(t => t.MediaType).ThenBy(t => t.LinkerName)
                .Select(t => new MetricSample(t.Count, [("media_type", t.MediaType ?? "unknown"), ("library", t.LinkerName ?? "")]))
                .ToList()));

        var grabs = await dbContext.Events.AsNoTracking()
            .Where(e => e.Kind == EventKind.Grabbed)
            .GroupBy(e => new { e.Cost, e.WedgeUsed })
            .Select(g => new { g.Key.Cost, g.Key.WedgeUsed, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var grabsByCost = grabs
            .GroupBy(g => CostLabels[LibraryStatistics.CostColumn(g.Cost, g.WedgeUsed)])
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        metrics.Add(new Metric("fetchshelf_grabbed_total", "counter", "Torrents grabbed, by what they cost.",
            CostLabels.Values.Select(cost => new MetricSample(grabsByCost.GetValueOrDefault(cost), [("cost", cost)])).ToList()));
        metrics.Add(new Metric("fetchshelf_linked_total", "counter", "Torrents linked into a library.",
            [new MetricSample(await dbContext.Events.CountAsync(e => e.Kind == EventKind.Linked, cancellationToken))]));

        var jobs = jobRegistry.GetAll().OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
        metrics.Add(new Metric("fetchshelf_job_last_run_timestamp_seconds", "gauge", "When each job's last run ended (or started, while it runs), by how it ended.",
            jobs.Where(j => j.LastRun is not null)
                .Select(j => new MetricSample(
                    (j.LastRun!.FinishedAt ?? j.LastRun.StartedAt).ToUnixTimeSeconds(),
                    [("job", j.Name), ("result", ResultLabel(j.LastRun.Result))]))
                .ToList()));
        metrics.Add(new Metric("fetchshelf_job_failing", "gauge", "1 while a job's runs keep failing, as the Jobs page marks it, else 0.",
            jobs.Select(j => new MetricSample(j.Failing is null ? 0 : 1, [("job", j.Name)])).ToList()));
        return metrics;
    }

    private static string ResultLabel(JobRunResult result) => result switch
    {
        JobRunResult.Success => "success",
        JobRunResult.Failure => "failure",
        JobRunResult.CompletedWithErrors => "completed_with_errors",
        JobRunResult.Running => "running",
        JobRunResult.Cancelled => "cancelled",
        _ => "interrupted",
    };
}

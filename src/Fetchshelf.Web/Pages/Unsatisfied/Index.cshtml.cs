using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Unsatisfied;

/// <summary>
/// One row of the page: Fetchshelf's torrents; every MAM torrent in qBittorrent (null when it couldn't be read, and in
/// the row for the Seeding job's estimate, which is before qBittorrent was read); and MAM's own count going forward (null
/// when MAM's count or qBittorrent hasn't been read, and in that row).
/// </summary>
/// <param name="IsEstimate">The row for the Seeding job's estimate, before now.</param>
public sealed record BurndownPageRow(BurndownRow Fetchshelf, BurndownRow? InClient, BurndownRow? Mam, bool IsEstimate = false);

/// <summary>
/// The Unsatisfied burndown: how many torrents will look unsatisfied each hour over the next three days
/// (<see cref="UnsatisfiedBurndown"/>), for Fetchshelf's torrents from the Seeding job's last estimates, for every
/// torrent in qBittorrent with MAM as its tracker, from qBittorrent's numbers now, and for MAM's own count as last read,
/// less what qBittorrent's numbers say is satisfied since. Only with a `[seeding]` section.
/// </summary>
public class IndexModel(
    FetchshelfDbContext dbContext, FetchshelfSettings settings, IReadOnlyList<QBittorrentClient> qbittorrentClients, MamClient mamClient,
    TimeProvider timeProvider) : PageModel
{
    public IReadOnlyList<BurndownPageRow> Rows { get; private set; } = [];

    public SeedingEstimateView Estimate { get; private set; } = null!;

    /// <summary>When the Seeding job last estimated a torrent; null when it hasn't yet.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>MAM's own count of unsatisfied torrents, with when it was read; null before the first read.</summary>
    public (long Count, DateTimeOffset ReadAt)? MamReported { get; private set; }

    /// <summary>Why qBittorrent couldn't be read, leaving the qBittorrent and MAM columns empty; null when it was.</summary>
    public string? QBittorrentError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (settings.Seeding is not { } seeding)
        {
            return NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var requirement = seeding.Requirement;
        Estimate = await SeedingEstimateView.LoadAsync(dbContext, mamClient.LastAccountState?.State, cancellationToken);
        CheckedAt = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.SeedingCheckedAt != null)
            .Select(t => t.SeedingCheckedAt)
            .ToListAsync(cancellationToken) is { Count: > 0 } checkedTimes ? checkedTimes.Max() : null;

        var unsatisfied = new[] { nameof(SeedingState.SeedingUnsatisfied), nameof(SeedingState.NotSeedingUnsatisfied) };
        var stored = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.SeedingState != null && unsatisfied.Contains(t.SeedingState))
            .Select(t => new { t.SeedingState, t.SeedingSeconds, t.SeedingSince, t.SeedingCheckedAt })
            .ToListAsync(cancellationToken);
        // The first row is the Seeding job's estimate itself, so the table starts from the count above it; the rest are
        // now and each hour after, which the other columns start from.
        var times = UnsatisfiedBurndown.Times(now);
        var fromEstimate = CheckedAt is { } checkedAt && UnsatisfiedBurndown.ToTheMinute(checkedAt) < times[0];
        var fetchshelf = UnsatisfiedBurndown.Build(
            stored.Where(t => t.SeedingSince is not null && t.SeedingCheckedAt is not null)
                .Select(t => new SeedingSnapshot(
                    Enum.Parse<SeedingState>(t.SeedingState!), TimeSpan.FromSeconds(t.SeedingSeconds ?? 0), t.SeedingSince!.Value, t.SeedingCheckedAt!.Value)),
            fromEstimate ? [UnsatisfiedBurndown.ToTheMinute(CheckedAt!.Value), .. times] : times, requirement);

        if (mamClient.LastAccountState is { } account)
        {
            MamReported = (account.State.UnsatCount, account.FetchedAt);
        }

        var mamTorrents = await MamTorrentsAsync(now, requirement, cancellationToken);
        var inClient = mamTorrents is null ? null : UnsatisfiedBurndown.Build(mamTorrents, times, requirement);
        var mam = mamTorrents is null || MamReported is not { } reported
            ? null
            : UnsatisfiedBurndown.FromReported(reported.Count, reported.ReadAt, mamTorrents, times, requirement);
        var offset = fromEstimate ? 1 : 0;
        Rows = fetchshelf.Select((row, i) => i < offset
                ? new BurndownPageRow(row, null, null, IsEstimate: true)
                : new BurndownPageRow(row, inClient?[i - offset], mam?[i - offset]))
            .ToList();
        return Page();
    }

    /// <summary>
    /// Every finished torrent in qBittorrent with MAM as its tracker, satisfied ones included, estimated from qBittorrent's
    /// numbers now; null, with
    /// <see cref="QBittorrentError"/> set, when a qBittorrent can't be read.
    /// </summary>
    private async Task<List<SeedingSnapshot>?> MamTorrentsAsync(DateTimeOffset now, SeedingRequirement requirement, CancellationToken cancellationToken)
    {
        var torrents = new Dictionary<string, QBittorrentTorrent>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var client in qbittorrentClients)
            {
                foreach (var torrent in await client.ListTorrentsAsync(cancellationToken))
                {
                    torrents.TryAdd(torrent.Hash, torrent);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            && !cancellationToken.IsCancellationRequested)
        {
            QBittorrentError = ex.Message;
            return null;
        }

        return torrents.Values
            .Where(t => t.IsMam && t.IsFinished)
            .Select(t =>
            {
                var seeded = TimeSpan.FromSeconds(t.SeedingTime);
                var since = DateTimeOffset.FromUnixTimeSeconds(t.AddedOn);
                var estimate = SeedingEstimator.Estimate(seeded, since, SeedingEstimator.IsSeeding(t.State), previous: null, now, requirement);
                return new SeedingSnapshot(estimate.State, seeded, since, now);
            })
            .ToList();
    }

    /// <summary>"2 satisfied, 1 past the period", for a count lower than the hour before.</summary>
    public static string Change(BurndownRow row) => string.Join(", ", new[]
    {
        row.Satisfied == 0 ? null : $"{row.Satisfied} satisfied",
        row.PastThePeriod == 0 ? null : $"{row.PastThePeriod} past the period",
    }.OfType<string>());
}

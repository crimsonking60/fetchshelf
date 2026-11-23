using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Unsatisfied;

/// <summary>One hour of the page: Fetchshelf's torrents, and every MAM torrent in qBittorrent (null when it couldn't be read).</summary>
public sealed record BurndownPageRow(BurndownRow Fetchshelf, BurndownRow? Mam);

/// <summary>
/// The Unsatisfied burndown: how many torrents will look unsatisfied each hour over the next three days
/// (<see cref="UnsatisfiedBurndown"/>), for Fetchshelf's torrents from the Seeding job's last estimates, and for every
/// torrent in qBittorrent with MAM as its tracker, from qBittorrent's numbers now. Only with a `[seeding]` section.
/// </summary>
public class IndexModel(
    FetchshelfDbContext dbContext, FetchshelfSettings settings, IReadOnlyList<QBittorrentClient> qbittorrentClients, MamClient mamClient,
    TimeProvider timeProvider) : PageModel
{
    public IReadOnlyList<BurndownPageRow> Rows { get; private set; } = [];

    public SeedingEstimateView Estimate { get; private set; } = null!;

    /// <summary>When the Seeding job last estimated a torrent; null when it hasn't yet.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>Why qBittorrent couldn't be read, leaving the MAM column empty; null when it was.</summary>
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
        var fetchshelf = UnsatisfiedBurndown.Build(
            stored.Where(t => t.SeedingSince is not null && t.SeedingCheckedAt is not null)
                .Select(t => new SeedingSnapshot(
                    Enum.Parse<SeedingState>(t.SeedingState!), TimeSpan.FromSeconds(t.SeedingSeconds ?? 0), t.SeedingSince!.Value, t.SeedingCheckedAt!.Value)),
            now, requirement);

        var mamTorrents = await MamTorrentsAsync(now, requirement, cancellationToken);
        var mam = mamTorrents is null ? null : UnsatisfiedBurndown.Build(mamTorrents, now, requirement);
        Rows = fetchshelf.Select((row, i) => new BurndownPageRow(row, mam?[i])).ToList();
        return Page();
    }

    /// <summary>
    /// Every finished torrent in qBittorrent with MAM as its tracker, estimated from qBittorrent's numbers now; null, with
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

using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one Seeding run found and did.</summary>
/// <param name="Checked">Torrents estimated.</param>
/// <param name="LookUnsatisfied">Torrents not yet satisfied and still within the period, seeding or not.</param>
/// <param name="NotSeeding">Of those, the ones not seeding now.</param>
/// <param name="PastThePeriod">Torrents that never reached the requirement in time: likely hit-and-runs.</param>
/// <param name="NewlySatisfied">Torrents that looked unsatisfied before this run and are satisfied now.</param>
/// <param name="OnCleanedApplied">Retired copies whose held-back `on_cleaned` was applied now that they are satisfied.</param>
public sealed record SeedingRunSummary(int Checked, int LookUnsatisfied, int NotSeeding, int PastThePeriod, int NewlySatisfied, int OnCleanedApplied);

/// <summary>
/// The Seeding job: estimates, from qBittorrent's seeding time, which of Fetchshelf's torrents still need seeding to
/// satisfy MAM (<see cref="SeedingEstimator"/>), and stores it on each torrent. With `tag_unsatisfied` it tags them in
/// qBittorrent; it applies `on_cleaned` to retired copies the Clean job held it back for once they are satisfied, and
/// sends `unsatisfied_not_seeding` for one that stops seeding too early. It never talks to MAM.
/// </summary>
public sealed class SeedingRunner(
    FetchshelfDbContext dbContext,
    SeedingSettings settings,
    INotificationSink notifications,
    TimeProvider timeProvider,
    ILogger<SeedingRunner>? logger = null)
{
    private readonly ILogger<SeedingRunner> _logger = logger ?? NullLogger<SeedingRunner>.Instance;

    private sealed record InClient(QBittorrentTorrent Torrent, QBittorrentConnection Connection, QBittorrentClient Client);

    public async Task<SeedingRunSummary> RunAsync(
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var requirement = settings.Requirement;
        var inClients = new Dictionary<string, InClient>(StringComparer.OrdinalIgnoreCase);
        foreach (var (connection, client) in connections)
        {
            foreach (var torrent in await client.ListTorrentsAsync(cancellationToken))
            {
                inClients.TryAdd(torrent.Hash, new InClient(torrent, connection, client));
            }
        }

        // Torrents in a client now, and those estimated before, which may have left it since.
        var hashes = inClients.Keys.Select(hash => hash.ToLowerInvariant()).ToList();
        var torrents = await dbContext.Torrents
            .Where(t => t.SeedingState != null || hashes.Contains(t.Id))
            .ToListAsync(cancellationToken);

        int checkedCount = 0, unsatisfied = 0, notSeeding = 0, pastThePeriod = 0, newlySatisfied = 0, onCleanedApplied = 0;
        foreach (var torrent in torrents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = Enum.TryParse<SeedingState>(torrent.SeedingState, out var parsed) ? parsed : (SeedingState?)null;
            inClients.TryGetValue(torrent.Id, out var found);
            SeedingEstimate estimate;
            if (found is not null)
            {
                if (!found.Torrent.IsFinished)
                {
                    continue; // still downloading: nothing to estimate yet
                }

                var since = DateTimeOffset.FromUnixTimeSeconds(found.Torrent.AddedOn);
                var seeded = TimeSpan.FromSeconds(found.Torrent.SeedingTime);
                estimate = SeedingEstimator.Estimate(seeded, since, SeedingEstimator.IsSeeding(found.Torrent.State), previous, now, requirement);
                (torrent.SeedingSeconds, torrent.SeedingSince) = (found.Torrent.SeedingTime, since);
            }
            else if (previous is not null && torrent.SeedingSince is { } since)
            {
                // Gone from qBittorrent: its seeding time stays what it was, and it can't be seeding.
                estimate = SeedingEstimator.Estimate(TimeSpan.FromSeconds(torrent.SeedingSeconds ?? 0), since, seedingNow: false, previous, now, requirement);
            }
            else
            {
                continue;
            }

            checkedCount++;
            torrent.SeedingState = estimate.State.ToString();
            torrent.SeedingCheckedAt = now;
            unsatisfied += estimate.LooksUnsatisfied ? 1 : 0;
            notSeeding += estimate.State == SeedingState.NotSeedingUnsatisfied ? 1 : 0;
            pastThePeriod += estimate.State == SeedingState.PastThePeriod ? 1 : 0;
            if (previous is not null and not SeedingState.Satisfied && estimate.State == SeedingState.Satisfied)
            {
                newlySatisfied++;
            }

            if (estimate.State == SeedingState.NotSeedingUnsatisfied && previous != SeedingState.NotSeedingUnsatisfied)
            {
                notifications.Publish(SeedingNotifications.NotSeeding(torrent.Id, torrent.MamId, torrent.Title, estimate, requirement, now));
            }

            if (found is not null)
            {
                await TagAsync(found, estimate, cancellationToken);
            }

            if (torrent.OnCleanedHeld && estimate.State == SeedingState.Satisfied)
            {
                onCleanedApplied += await ApplyHeldOnCleanedAsync(torrent, found, cancellationToken) ? 1 : 0;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var summary = new SeedingRunSummary(checkedCount, unsatisfied, notSeeding, pastThePeriod, newlySatisfied, onCleanedApplied);
        _logger.LogInformation(
            "Seeding: {Checked} torrents checked; {Unsatisfied} look unsatisfied ({NotSeeding} not seeding), {Past} past the period, {Satisfied} newly satisfied",
            summary.Checked, summary.LookUnsatisfied, summary.NotSeeding, summary.PastThePeriod, summary.NewlySatisfied);
        return summary;
    }

    /// <summary>Puts `tag_unsatisfied` on a torrent that isn't satisfied and takes it off one that is, only where that changes anything.</summary>
    private async Task TagAsync(InClient found, SeedingEstimate estimate, CancellationToken cancellationToken)
    {
        if (settings.TagUnsatisfied is not { } tag)
        {
            return;
        }

        var wanted = estimate.State != SeedingState.Satisfied;
        var has = found.Torrent.TagList.Contains(tag, StringComparer.Ordinal);
        try
        {
            if (wanted && !has)
            {
                await found.Client.AddTagsAsync(found.Torrent.Hash, [tag], cancellationToken);
            }
            else if (!wanted && has)
            {
                await found.Client.RemoveTagsAsync(found.Torrent.Hash, [tag], cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            // One torrent's tag must not stop the run; the next run tries again.
            _logger.LogWarning(ex, "Could not change the {Tag} tag on {Hash}: {Error}", tag, found.Torrent.Hash, ex.Message);
        }
    }

    /// <summary>Applies `on_cleaned`, which the Clean job held back while the copy looked unsatisfied. True when it was applied.</summary>
    private async Task<bool> ApplyHeldOnCleanedAsync(TorrentEntity torrent, InClient? found, CancellationToken cancellationToken)
    {
        torrent.OnCleanedHeld = false;
        if (found?.Connection.OnCleaned is not { } setting)
        {
            _logger.LogInformation("{Title} is satisfied now, but is no longer in qBittorrent, so on_cleaned is not applied", torrent.Title);
            return false;
        }

        try
        {
            await QBittorrentActions.ApplyAsync(found.Client, torrent.Id, setting, cancellationToken);
            _logger.LogInformation("{Title} is satisfied now, so on_cleaned was applied: {Setting}", torrent.Title, QBittorrentActions.Describe(setting));
            return true;
        }
        catch (HttpRequestException ex)
        {
            torrent.OnCleanedHeld = true; // tried again next run
            _logger.LogWarning(ex, "Could not apply on_cleaned to {Title}: {Error}", torrent.Title, ex.Message);
            return false;
        }
    }
}

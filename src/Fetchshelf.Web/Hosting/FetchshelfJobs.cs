using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data;
using Fetchshelf.Data.Account;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web.Hosting;

/// <summary>
/// The jobs Fetchshelf runs, registered by name in <see cref="IJobRegistry"/>, and which jobs start which: Link starts
/// Clean, and a run that selects something starts Download. The names are <see cref="JobNames"/> and
/// <see cref="AutoDownloaderJobNames"/>; <see cref="JobServices"/> schedules them.
/// </summary>
public static class FetchshelfJobs
{
    public static void RegisterOrganizerJobs(IServiceProvider services, FetchshelfSettings settings, string dataFilePath, string? backupFolder)
    {
        var jobRegistry = services.GetRequiredService<IJobRegistry>();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var qbittorrentFactory = services.GetRequiredService<QBittorrentClientFactory>();

        jobRegistry.Register(JobNames.Link, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var linkRunner = scope.ServiceProvider.GetRequiredService<LinkRunner>();
            var total = new LinkRunSummary(0, 0);
            foreach (var (connection, client) in QBittorrentConnections.Build(settings, qbittorrentFactory))
            {
                var summary = await linkRunner.RunAsync(connection, client, ct);
                total = new LinkRunSummary(
                    total.Linked + summary.Linked, total.Failed + summary.Failed,
                    total.WaitingForLookup + summary.WaitingForLookup, total.WaitingForMam + summary.WaitingForMam);
            }

            // Clean is a run of its own, so cancelling this one doesn't cancel it; this run only stops waiting for it.
            await jobRegistry.TriggerAsync(JobNames.Clean, JobTrigger.Chained).WaitAsync(ct);

            return JobOutcomes.Link(total);
        }, "Moves finished downloads from qBittorrent into your library folders.");

        jobRegistry.Register(JobNames.Clean, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var cleaner = scope.ServiceProvider.GetRequiredService<Cleaner>();
            return JobOutcomes.Clean(await cleaner.RunAsync(QBittorrentConnections.Build(settings, qbittorrentFactory), ct));
        }, "Retires superseded duplicate torrents once a newer copy is linked.");

        if (settings.Audiobookshelf is not null)
        {
            jobRegistry.Register(JobNames.Audiobookshelf, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var matcher = scope.ServiceProvider.GetRequiredService<AbsMatcher>();
                return JobOutcomes.Audiobookshelf(await matcher.RunAsync(ct));
            }, "Matches linked torrents to their Audiobookshelf library item.");
        }

        if (settings.Seeding is not null)
        {
            jobRegistry.Register(JobNames.Seeding, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var seeding = scope.ServiceProvider.GetRequiredService<SeedingRunner>();
                return JobOutcomes.Seeding(await seeding.RunAsync(QBittorrentConnections.Build(settings, qbittorrentFactory), ct));
            }, "Estimates from qBittorrent's seeding time which torrents still need seeding to satisfy MAM.");
        }

        if (settings.Backup is { } backupSettings && backupFolder is not null)
        {
            var timeProvider = services.GetRequiredService<TimeProvider>();
            jobRegistry.Register(JobNames.Backup, ct => Task.Run(
                () => (JobOutcome?)JobOutcomes.Backup(DatabaseBackup.RunScheduled(dataFilePath, backupFolder, backupSettings.Keep, timeProvider.GetUtcNow())), ct),
                "Copies the database into the [backup] folder while Fetchshelf runs, and keeps the newest ones.");
        }
    }

    public static void RegisterAutoDownloaderJobs(IServiceProvider services, FetchshelfSettings settings)
    {
        var jobRegistry = services.GetRequiredService<IJobRegistry>();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var ignoreTorrents = settings.IgnoreTorrents.Select(long.Parse).ToList();

        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            var entry = settings.Autograb[i];
            var jobName = AutoDownloaderJobNames.Autograb(entry, i);
            jobRegistry.Register(jobName, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<AutograbberRunner>();
                var summary = await runner.RunAsync(entry, settings.UnsatBuffer, ignoreTorrents, ct);
                if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
                {
                    await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
                }

                return JobOutcomes.Autograb(summary);
            }, "Searches MAM for new matches and queues them for download.");
        }

        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var entry = settings.HardcoverList[i];
            var jobName = AutoDownloaderJobNames.HardcoverList(entry, i);
            jobRegistry.Register(jobName, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<HardcoverListImporter>();
                var summary = await importer.RunAsync(entry, jobName, settings.UnsatBuffer, ignoreTorrents, ct);
                if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
                {
                    await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
                }

                return JobOutcomes.HardcoverList(summary);
            }, "Searches MAM for the books on a Hardcover shelf or list and queues them for download.");
        }

        if (settings.Follow is not null)
        {
            jobRegistry.Register(JobNames.Follow, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<FollowRunner>();
                var summary = await runner.RunAsync(settings.UnsatBuffer, ignoreTorrents, ct);
                if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
                {
                    await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
                }

                return JobOutcomes.Follow(summary);
            }, "Searches MAM for the followed series and authors, and selects or lists the books the library is missing.");
        }

        if (settings.Upgrade is not null)
        {
            jobRegistry.Register(JobNames.Upgrade, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<UpgradeRunner>();
                var summary = await runner.RunAsync(settings.UnsatBuffer, ignoreTorrents, ct);
                if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
                {
                    await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
                }

                return JobOutcomes.Upgrade(summary);
            }, "Searches MAM for better formats of books in the library, such as m4b for an mp3 copy, and selects them when a grab block's cost allows.");
        }

        if (settings.Bonus is not null)
        {
            jobRegistry.Register(JobNames.Bonus, async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var summary = await scope.ServiceProvider.GetRequiredService<BonusRunner>().RunAsync(ct);
                return JobOutcomes.Bonus(summary);
            }, "Spends bonus points as the [bonus] section says: a wedge when one is due, a VIP top-up, then upload credit, keeping keep_points back.");
        }

        var budgetSpent = services.GetRequiredService<BudgetSpentWatcher>();
        jobRegistry.Register(JobNames.Download, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var downloadRunner = scope.ServiceProvider.GetRequiredService<DownloadRunner>();
            var summary = await downloadRunner.RunAsync(ct);

            // Told once when selections start waiting for budget, not on every run they keep waiting. A run MAM stopped
            // didn't try them all, so it says nothing about how many wait.
            if (!summary.StoppedByMam)
            {
                await budgetSpent.CheckAsync(summary.LeftQueued, summary.LimitedBy);
            }

            return JobOutcomes.Download(summary);
        }, "Sends queued, selected torrents to qBittorrent.");
    }

    /// <summary>
    /// Hands every account read to <see cref="AccountSnapshotRecorder"/>, which keeps the day's snapshot. The handler runs
    /// on the thread of the MAM call, so it only hands the read over.
    /// </summary>
    public static void ConnectAccountSnapshots(IServiceProvider services)
    {
        var recorder = services.GetRequiredService<AccountSnapshotRecorder>();
        services.GetRequiredService<MamClient>().AccountStateRead += (previous, current) => recorder.Enqueue(current);
    }

    /// <summary>
    /// Connects what MAM answers to notifications: a refused `mam_id`, a rate limit, and each account read, which is checked
    /// for a new hit-and-run. The handlers run on the thread of the MAM call, so each only hands something over: a
    /// notification to the queue, an account read to <see cref="HitAndRunWatcher"/>'s background check.
    /// </summary>
    public static void ConnectMamNotifications(IServiceProvider services)
    {
        var notifier = services.GetRequiredService<Notifier>();
        var mam = services.GetRequiredService<MamClient>();
        var throttle = new NotificationThrottle();
        mam.Refused += refusal =>
        {
            // A 403 is raised once per pause. A 406 pauses nothing and comes with every call, and a 429 may come every minute,
            // so those are told at most once in a while.
            var (key, interval, notification) = refusal.Kind switch
            {
                MamRefusalKind.Forbidden => ("mam_refused.403", TimeSpan.FromMinutes(30), AccountNotificationRules.MamRefused(refusal.At, refusal.PausedUntil ?? refusal.At)),
                MamRefusalKind.Blocked => ("mam_refused.406", TimeSpan.FromMinutes(30), AccountNotificationRules.MamBlocked(refusal.At)),
                _ => ("rate_limited", TimeSpan.FromHours(1), AccountNotificationRules.RateLimited(refusal.At)),
            };
            if (throttle.Allow(key, interval, refusal.At))
            {
                notifier.Publish(notification);
            }
        };

        var hitAndRuns = services.GetRequiredService<HitAndRunWatcher>();
        mam.AccountStateRead += (previous, current) => hitAndRuns.Enqueue(current.State);
    }
}

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

            var covers = settings.CoversFromMam ? await scope.ServiceProvider.GetRequiredService<CoverWriter>().RunAsync(ct) : null;

            // Clean is a run of its own, so cancelling this one doesn't cancel it; this run only stops waiting for it.
            await jobRegistry.TriggerAsync(JobNames.Clean, JobTrigger.Chained).WaitAsync(ct);

            return JobOutcomes.Link(total, covers);
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
            RegisterSelecting(jobRegistry, scopeFactory, settings, jobName, entry.DryRun, async (scope, dryRun, ct) =>
            {
                var summary = await scope.ServiceProvider.GetRequiredService<AutograbberRunner>()
                    .RunAsync(entry, settings.UnsatBuffer, ignoreTorrents, ct, dryRun);
                return (summary.SelectedForDownload, () => JobOutcomes.Autograb(summary));
            }, "Searches MAM for new matches and queues them for download.");
        }

        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var entry = settings.HardcoverList[i];
            RegisterList(jobRegistry, scopeFactory, settings, ignoreTorrents, ListDefinition.ForHardcover(entry, i),
                scope => new HardcoverListSource(scope.ServiceProvider.GetRequiredService<HardcoverBookSource>(), entry),
                "Searches MAM for the books on a Hardcover shelf or list and queues them for download.");
        }

        for (var i = 0; i < settings.FileList.Count; i++)
        {
            var entry = settings.FileList[i];
            RegisterList(jobRegistry, scopeFactory, settings, ignoreTorrents, ListDefinition.ForFile(entry, i),
                scope => new FileListSource(entry, scope.ServiceProvider.GetRequiredService<ILogger<FileListSource>>()),
                "Searches MAM for the books in a list file and queues them for download.");
        }

        foreach (var entry in settings.BookmarksList)
        {
            var list = ListDefinition.ForBookmarks(entry);
            RegisterList(jobRegistry, scopeFactory, settings, ignoreTorrents, list,
                scope => new MamBookmarksSource(
                    scope.ServiceProvider.GetRequiredService<MamClient>(), scope.ServiceProvider.GetRequiredService<MamAccessGate>(),
                    list.DisplayName, logger: scope.ServiceProvider.GetRequiredService<ILogger<MamBookmarksSource>>()),
                "Reads the torrents bookmarked on MAM and queues those its grab blocks take for download.");
        }

        if (settings.Follow is { } follow)
        {
            RegisterSelecting(jobRegistry, scopeFactory, settings, JobNames.Follow, follow.DryRun, async (scope, dryRun, ct) =>
            {
                var summary = await scope.ServiceProvider.GetRequiredService<FollowRunner>().RunAsync(settings.UnsatBuffer, ignoreTorrents, ct, dryRun);
                return (summary.SelectedForDownload, () => JobOutcomes.Follow(summary));
            }, "Searches MAM for the followed series and authors, and selects or lists the books the library is missing.");
        }

        if (settings.Upgrade is not null)
        {
            RegisterSelecting(jobRegistry, scopeFactory, settings, JobNames.Upgrade, settings.Upgrade.DryRun, async (scope, dryRun, ct) =>
            {
                var summary = await scope.ServiceProvider.GetRequiredService<UpgradeRunner>().RunAsync(settings.UnsatBuffer, ignoreTorrents, ct, dryRun);
                return (summary.SelectedForDownload, () => JobOutcomes.Upgrade(summary));
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

    /// <summary>Registers a list's job: <paramref name="source"/> gives the scope's reader of the list's books.</summary>
    private static void RegisterList(
        IJobRegistry jobRegistry, IServiceScopeFactory scopeFactory, FetchshelfSettings settings, IReadOnlyList<long> ignoreTorrents,
        ListDefinition list, Func<IServiceScope, IListBookSource> source, string description) =>
        RegisterSelecting(jobRegistry, scopeFactory, settings, list.JobName, list.DryRun, async (scope, dryRun, ct) =>
        {
            var summary = await scope.ServiceProvider.GetRequiredService<ListImporter>()
                .RunAsync(list, source(scope), settings.UnsatBuffer, ignoreTorrents, ct, dryRun);
            return (summary.SelectedForDownload, () => JobOutcomes.List(summary));
        }, description);

    /// <summary>
    /// Registers a job that selects torrents: an autograb, a list, Follow or Upgrade. <paramref name="run"/> runs
    /// it in a scope of its own, as a dry run when told to, and returns how many it selected for download and its outcome.
    /// Each run's outcome lists what <see cref="SelectionRunner"/> selected, or would have, and turned down. A real run that
    /// selected something starts a Download run when `download_after_selection` is on. A block configured with
    /// `dry_run = true` dry-runs every time, so it gets no separate dry run.
    /// </summary>
    private static void RegisterSelecting(
        IJobRegistry jobRegistry, IServiceScopeFactory scopeFactory, FetchshelfSettings settings, string jobName, bool configuredDryRun,
        Func<IServiceScope, bool?, CancellationToken, Task<(int SelectedForDownload, Func<JobOutcome> Outcome)>> run, string description)
    {
        async Task<JobOutcome?> RunAsync(bool? dryRun, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var (selectedForDownload, outcome) = await run(scope, dryRun, ct);
            if (selectedForDownload > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
            }

            var details = scope.ServiceProvider.GetRequiredService<SelectionRunner>().Details;
            try
            {
                return outcome() with { Details = details };
            }
            catch (JobCompletedWithErrorsException ex)
            {
                throw new JobCompletedWithErrorsException(ex.Message, ex.Counts, details);
            }
        }

        jobRegistry.Register(
            jobName, ct => RunAsync(null, ct),
            configuredDryRun ? $"{description} dry_run is set, so every run is a dry run." : description,
            configuredDryRun ? null : ct => RunAsync(true, ct));
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

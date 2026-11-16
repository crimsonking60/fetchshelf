using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web.Hosting;

/// <summary>
/// The job registry and what starts the jobs: one <see cref="IntervalJobScheduler"/> per job that runs on an interval, and
/// the watcher that follows downloads into the library. Which jobs exist is <see cref="FetchshelfJobs"/>'s business.
/// </summary>
public static class JobServices
{
    /// <summary>How far apart jobs that are due first run after start.</summary>
    public static readonly TimeSpan StartupStagger = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddFetchshelfJobs(this IServiceCollection services, FetchshelfSettings settings)
    {
        // Each Download run wakes the watcher that follows downloads into the library (see DownloadProgressWatcher below).
        services.AddSingleton<DownloadWatchSignal>();
        services.AddSingleton<IJobRunListener>(sp => new DownloadWatchWaker(sp.GetRequiredService<DownloadWatchSignal>()));

        // The registry is told which failures are MAM refusing the session or account, so the Errors page can group them, and
        // who to tell when a run ends, so a job that starts failing can be sent to a phone.
        services.AddSingleton<IJobRegistry>(sp => new JobRegistry(
            sp.GetRequiredService<ILogger<JobRegistry>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IJobRunStore>(),
            JobFailureClassifier.Classify,
            sp.GetServices<IJobRunListener>()));

        if (settings.BackgroundJobsEnabled)
        {
            AddSchedulers(services, settings);
        }

        return services;
    }

    private static void AddSchedulers(IServiceCollection services, FetchshelfSettings settings)
    {
        // AddSingleton<IHostedService>, not AddHostedService: with a factory lambda, AddHostedService
        // de-duplicates by implementation type, so only the first IntervalJobScheduler would be kept.
        void AddScheduler(string jobName, int intervalMinutes, int startupPosition) =>
            services.AddSingleton<IHostedService>(sp => new IntervalJobScheduler(
                sp.GetRequiredService<IJobRegistry>(), jobName, TimeSpan.FromMinutes(intervalMinutes),
                StartupStagger * startupPosition, sp.GetRequiredService<TimeProvider>()));

        // A job that is due, or has never run, first runs soon after start: Link first, then each MAM job in
        // configuration order, then Download, one stagger apart. MamAccessGate already keeps the MAM jobs from
        // overlapping; the stagger keeps their order predictable and the log readable.
        var startupPosition = 1;
        AddScheduler(JobNames.Link, settings.LinkInterval, startupPosition);

        if (settings.Backup is { Interval: > 0 } scheduledBackup)
        {
            // The Backup job talks to no other service, so it starts alongside Link.
            AddScheduler(JobNames.Backup, scheduledBackup.Interval, startupPosition);
        }

        if (settings.Audiobookshelf is { } scheduledAbsSettings)
        {
            // Audiobookshelf doesn't talk to MAM, so it starts alongside Link.
            AddScheduler(JobNames.Audiobookshelf, scheduledAbsSettings.Interval, startupPosition);
        }

        var mamJobs = settings.Autograb.Select((entry, i) => (AutoDownloaderJobNames.Autograb(entry, i), entry.SearchInterval))
            .Concat(settings.Snatchlist.Select((entry, i) => (AutoDownloaderJobNames.Snatchlist(entry, i), entry.SearchInterval)))
            .Concat(settings.HardcoverList.Select((entry, i) => (AutoDownloaderJobNames.HardcoverList(entry, i), entry.SearchInterval)))
            .Concat(settings.Follow is { } scheduledFollow ? [(JobNames.Follow, scheduledFollow.SearchInterval)] : [])
            .Concat(settings.Upgrade is { } scheduledUpgrade ? [(JobNames.Upgrade, scheduledUpgrade.SearchInterval)] : [])
            .Concat(settings.Bonus is { } scheduledBonus ? [(JobNames.Bonus, (int?)scheduledBonus.BonusInterval)] : []);
        foreach (var (jobName, searchInterval) in mamJobs)
        {
            var intervalMinutes = searchInterval ?? settings.SearchInterval;
            if (intervalMinutes > 0)
            {
                AddScheduler(jobName, intervalMinutes, ++startupPosition);
            }
        }

        if (settings.SearchInterval > 0)
        {
            AddScheduler(JobNames.Download, settings.SearchInterval, ++startupPosition);
        }

        // Follows what is downloading into the library, so it is linked minutes after it finishes, whether a page is open
        // or not, rather than at the Link job's next interval.
        if (settings.QBittorrent.Count > 0)
        {
            services.AddSingleton<IHostedService>(sp => new DownloadProgressWatcher(
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IJobRegistry>(), sp.GetRequiredService<DownloadWatchSignal>(),
                sp.GetRequiredService<ILogger<DownloadProgressWatcher>>(), sp.GetRequiredService<TimeProvider>()));
        }
    }
}

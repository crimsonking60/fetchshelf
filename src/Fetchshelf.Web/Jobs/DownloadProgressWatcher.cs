using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Jobs;

/// <summary>Wakes the <see cref="DownloadProgressWatcher"/>: something may have started downloading.</summary>
public sealed class DownloadWatchSignal
{
    private readonly SemaphoreSlim _woken = new(0, 1);

    public void Wake()
    {
        try
        {
            _woken.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already woken; one wake-up covers any number of reasons.
        }
    }

    /// <summary>Waits for a wake-up, at most <paramref name="timeout"/>; true when woken.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _woken.WaitAsync(timeout, cancellationToken);
}

/// <summary>Wakes the <see cref="DownloadProgressWatcher"/> each time a Download job run ends: it may have added torrents.</summary>
public sealed class DownloadWatchWaker(DownloadWatchSignal signal) : IJobRunListener
{
    public void RunFinished(JobRunFinished finished)
    {
        if (finished.JobName == JobNames.Download)
        {
            signal.Wake();
        }
    }
}

/// <summary>
/// Follows downloads on the server, so a torrent moves on to linked whether a page showing it is open or not: while any
/// torrent added to qBittorrent within <see cref="DownloadProgressChecker.WatchFor"/> is still downloading, asks where it
/// stands (<see cref="DownloadProgressChecker"/>), and starts the Link job as soon as one has finished. Every
/// <see cref="FastPace"/> for <see cref="FastFor"/> after a Download run, then every <see cref="SlowPace"/>; asleep while
/// nothing is downloading. The status badges only read what it stores. Not a job of its own on the Jobs page: its rounds
/// are too frequent to be worth a run each, and the Link runs it starts show there.
/// </summary>
public sealed class DownloadProgressWatcher(
    IServiceScopeFactory scopeFactory, IJobRegistry jobRegistry, DownloadWatchSignal signal, ILogger<DownloadProgressWatcher> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public static readonly TimeSpan FastPace = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FastFor = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SlowPace = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A first round straight away: downloads going when Fetchshelf stopped are followed again.
        var fastUntil = _time.GetUtcNow() + FastFor;
        string? lastError = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await CheckAsync(stoppingToken);
                if (result.Error != lastError)
                {
                    // Said once when it starts and once when it stops, not every round.
                    if (result.Error is not null)
                    {
                        logger.LogWarning("Following downloads: qBittorrent could not be asked: {Error}", result.Error);
                    }
                    else
                    {
                        logger.LogInformation("Following downloads: qBittorrent answers again");
                    }

                    lastError = result.Error;
                }

                logger.LogDebug("Following downloads: {Watched} downloading, {Finished} finished since the last round", result.Watched, result.Finished);
                if (result.Finished > 0 && jobRegistry.Get(JobNames.Link) is not null)
                {
                    logger.LogInformation("Following downloads: {Finished} finished in qBittorrent, starting the Link job", result.Finished);
                    _ = jobRegistry.TriggerOrQueueAsync(JobNames.Link, JobTrigger.Chained, CancellationToken.None);
                }

                var wait = result.Watched == 0 ? Timeout.InfiniteTimeSpan : _time.GetUtcNow() < fastUntil ? FastPace : SlowPace;
                if (await signal.WaitAsync(wait, stoppingToken))
                {
                    fastUntil = _time.GetUtcNow() + FastFor;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A round that fails, such as on a locked database, is tried again later rather than ending the watching.
                logger.LogWarning(ex, "Following downloads failed: {Error}", ex.Message);
                await signal.WaitAsync(SlowPace, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<DownloadProgressResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DownloadProgressChecker>().CheckAsync(cancellationToken);
    }
}

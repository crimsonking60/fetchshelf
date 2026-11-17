using System.Globalization;
using System.Threading.Channels;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Account;

/// <summary>
/// Keeps one snapshot of the account a UTC day, from the account reads the jobs make anyway: the first read of a day
/// adds the day's row, and later ones replace it, so the row holds the day's last value. A day without any read has
/// no row; nothing extra is asked of MAM.
/// </summary>
/// <remarks>
/// <see cref="Enqueue"/> is what MAM's account reads call: it only hands the read over, so the job that read the account
/// never waits on the database. <see cref="RunAsync"/>, a background loop, does the writing. Only the latest read waits:
/// an older one says nothing a newer one doesn't.
/// </remarks>
public sealed class AccountSnapshotRecorder(
    IDbContextFactory<FetchshelfDbContext> contextFactory, TimeProvider timeProvider, ILogger<AccountSnapshotRecorder>? logger = null)
{
    /// <summary>A read this soon after the day's row was written is skipped, so a busy day costs only a few writes.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(10);

    /// <summary>How many years of snapshots are kept; older days are deleted once a day.</summary>
    public const int YearsKept = 2;

    private readonly ILogger<AccountSnapshotRecorder> _logger = logger ?? NullLogger<AccountSnapshotRecorder>.Instance;

    private readonly Channel<MamAccountSnapshot> _reads = Channel.CreateBounded<MamAccountSnapshot>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private DateOnly? _lastPruned;

    /// <summary>The UTC day <paramref name="at"/> falls on, as the table stores it.</summary>
    public static string DayOf(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Hands an account read over for recording, and returns at once.</summary>
    public void Enqueue(MamAccountSnapshot read) => _reads.Writer.TryWrite(read);

    /// <summary>Records each read handed over, until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var read in _reads.Reader.ReadAllAsync(stoppingToken))
            {
                await RecordAsync(read);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping: a read not yet recorded is replaced by the next one anyway.
        }
    }

    /// <summary>Records one account read as its day's snapshot, then prunes once a day. It never throws.</summary>
    public async Task RecordAsync(MamAccountSnapshot read)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            var day = DayOf(read.FetchedAt);
            var row = await context.AccountSnapshots.FirstOrDefaultAsync(s => s.Day == day);
            if (row is not null && read.FetchedAt - row.TakenAt < MinimumGap)
            {
                return;
            }

            if (row is null)
            {
                row = new AccountSnapshotEntity { Day = day, TakenAt = read.FetchedAt };
                context.AccountSnapshots.Add(row);
            }

            Fill(row, read);
            await context.SaveChangesAsync();
            await PruneOnceADayAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the account snapshot: {Error}", ex.Message);
        }
    }

    private static void Fill(AccountSnapshotEntity row, MamAccountSnapshot read)
    {
        var state = read.State;
        row.TakenAt = read.FetchedAt;
        row.Ratio = state.Ratio;
        row.UploadedBytes = (long)state.UploadedBytes;
        row.DownloadedBytes = (long)state.DownloadedBytes;
        row.Wedges = state.Wedges;
        row.UnsatCount = state.UnsatCount;
        row.UnsatLimit = state.UnsatLimit;
        row.BonusPoints = state.BonusPoints;
        row.ClassName = state.ClassName;
    }

    /// <summary>Deletes the days older than <see cref="YearsKept"/> years, at most once a UTC day.</summary>
    private async Task PruneOnceADayAsync(FetchshelfDbContext context)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (_lastPruned == today)
        {
            return;
        }

        var oldestKept = today.AddYears(-YearsKept).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await context.AccountSnapshots.Where(s => string.Compare(s.Day, oldestKept) < 0).ExecuteDeleteAsync();
        _lastPruned = today;
    }
}

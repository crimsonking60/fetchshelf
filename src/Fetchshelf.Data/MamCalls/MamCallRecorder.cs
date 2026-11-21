using System.Threading.Channels;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.MamCalls;

/// <summary>
/// Writes every call made to MAM to the <c>MamCalls</c> table, for the MAM API page. <see cref="Enqueue"/> is what
/// <see cref="MamClient.CallCompleted"/> calls: it only hands the call over, so the caller never waits on the database.
/// <see cref="RunAsync"/>, a background loop, writes what is waiting in one save, and deletes rows older than
/// <see cref="DaysKept"/> days once a day.
/// </summary>
public sealed class MamCallRecorder(
    IDbContextFactory<FetchshelfDbContext> contextFactory, TimeProvider timeProvider, ILogger<MamCallRecorder>? logger = null)
{
    /// <summary>How many days of calls are kept.</summary>
    public const int DaysKept = 30;

    /// <summary>How many calls may wait to be written; past that the oldest are dropped rather than holding up a caller.</summary>
    private const int MaxWaiting = 10_000;

    private readonly ILogger<MamCallRecorder> _logger = logger ?? NullLogger<MamCallRecorder>.Instance;

    private readonly Channel<MamCallEntity> _calls = Channel.CreateBounded<MamCallEntity>(
        new BoundedChannelOptions(MaxWaiting) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private DateOnly? _lastPruned;

    /// <summary>Hands a call over for writing, and returns at once.</summary>
    /// <param name="call">The call, as the client reported it.</param>
    /// <param name="caller">Who held the MAM gate during the call, if anyone.</param>
    public void Enqueue(MamCall call, string? caller) => _calls.Writer.TryWrite(new MamCallEntity
    {
        StartedAtMs = call.StartedAt.ToUnixTimeMilliseconds(),
        DurationMs = (int)Math.Min(call.Duration.TotalMilliseconds, int.MaxValue),
        Method = call.Method,
        Host = call.Host,
        Path = call.Path,
        StatusCode = call.StatusCode,
        Failure = call.Failure,
        Caller = caller,
    });

    /// <summary>Writes the calls handed over, until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _calls.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteWaitingAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping: write what is still waiting, which is a moment's work.
            await WriteWaitingAsync();
        }
    }

    /// <summary>Writes every call waiting, then prunes once a day. It never throws.</summary>
    public async Task WriteWaitingAsync()
    {
        var batch = new List<MamCallEntity>();
        while (_calls.Reader.TryRead(out var call))
        {
            batch.Add(call);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            context.MamCalls.AddRange(batch);
            await context.SaveChangesAsync();
            await PruneOnceADayAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record {Count} MAM calls: {Error}", batch.Count, ex.Message);
        }
    }

    private async Task PruneOnceADayAsync(FetchshelfDbContext context)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (_lastPruned == today)
        {
            return;
        }

        var oldestKept = (now - TimeSpan.FromDays(DaysKept)).ToUnixTimeMilliseconds();
        await context.MamCalls.Where(c => c.StartedAtMs < oldestKept).ExecuteDeleteAsync();
        _lastPruned = today;
    }
}

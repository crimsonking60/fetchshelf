using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fetchshelf.Data;

/// <summary>
/// Saves committed together in one transaction instead of each with a disk sync of its own, for a job that saves once
/// per item over thousands of items. An open batch holds SQLite's write lock (EF begins with <c>BEGIN IMMEDIATE</c>), so
/// it is committed when it has <c>maxSize</c> items or is <c>maxAge</c> old, and each item's work is short.
/// <para>
/// A batch begun the moment the last one was committed takes the lock back before another writer can have it: a writer
/// waiting on <see cref="SqliteBusyTimeout"/> only tries again every so often (at most every 100 ms), and misses a gap of
/// microseconds every time, until it fails with "database is locked". So a batch begun within <see cref="YieldTime"/> of
/// the last commit waits out the rest of it first.
/// </para>
/// </summary>
/// <param name="yieldTime">
/// How long the lock is left free between two batches; <see cref="YieldTime"/> unless given. Tests whose items take no
/// time pass zero, as no other writer waits there.
/// </param>
public sealed class SqliteWriteBatch(DbContext dbContext, int maxSize, TimeSpan maxAge, TimeSpan? yieldTime = null)
{
    /// <summary>How long the write lock is left free between two batches: SQLite's longest wait between tries.</summary>
    public static readonly TimeSpan YieldTime = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _yieldTime = yieldTime ?? YieldTime;

    private IDbContextTransaction? _transaction;
    private int _count;
    private long _startedAt;
    private long? _committedAt;

    /// <summary>Whether a batch is open.</summary>
    public bool IsOpen => _transaction is not null;

    /// <summary>Begins a batch unless one is open, after leaving the lock free for the yield time since the last commit.</summary>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            return;
        }

        if (_committedAt is { } committedAt && Stopwatch.GetElapsedTime(committedAt) is var free && free < _yieldTime)
        {
            await Task.Delay(_yieldTime - free, cancellationToken);
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _startedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>One item is done: commits the batch when it is full or old enough.</summary>
    public async Task ItemDoneAsync()
    {
        if (_transaction is not null && (++_count >= maxSize || Stopwatch.GetElapsedTime(_startedAt) >= maxAge))
        {
            await CommitAsync();
        }
    }

    /// <summary>Commits the open batch, if any; saves until the next batch begins commit on their own.</summary>
    public async Task CommitAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(CancellationToken.None);
        await _transaction.DisposeAsync();
        _transaction = null;
        _count = 0;
        _committedAt = Stopwatch.GetTimestamp();
    }
}

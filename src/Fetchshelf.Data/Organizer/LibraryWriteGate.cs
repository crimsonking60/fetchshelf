namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Lets only one thing at a time place or remove a torrent's files in the libraries: the Link job, the Clean job, a bulk
/// action from the Torrents page, or a torrent page's own action. Taken per torrent, not per run, so a long Link run and
/// a bulk relink take turns instead of one waiting for the other to finish. A singleton.
/// </summary>
/// <remarks>
/// The callers take it, never <c>LinkRunner.RelinkAsync</c>, <c>Cleaner.UnlinkAsync</c> and the like themselves: the gate
/// isn't reentrant, and those are called from places that already hold it.
/// </remarks>
public sealed class LibraryWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Takes the gate if it is free, without waiting; dispose the result to let it go. Null when it is taken.</summary>
    public IDisposable? TryAcquire() => _semaphore.Wait(0) ? new Release(_semaphore) : null;

    /// <summary>Waits for the gate; dispose the result to let it go.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Release(_semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
